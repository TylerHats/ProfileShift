using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ProfileShift.Models;

namespace ProfileShift.Core
{
    public static class CredentialManagerExporter
    {
        // --- Win32 Credential Manager P/Invoke ---

        private const int CRED_TYPE_GENERIC = 1;
        private const int CRED_TYPE_DOMAIN_PASSWORD = 2;

        private const int CRED_PERSIST_SESSION = 1;
        private const int CRED_PERSIST_LOCAL_MACHINE = 2;
        private const int CRED_PERSIST_ENTERPRISE = 3;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct CREDENTIAL
        {
            public uint Flags;
            public uint Type;
            public string TargetName;
            public string Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public uint Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public string TargetAlias;
            public string UserName;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredEnumerateW(
            string? filter,
            uint flags,
            out int count,
            out IntPtr credentialsPtr);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern void CredFree(IntPtr buffer);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredWriteW(
            ref CREDENTIAL credential,
            uint flags);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CredDeleteW(
            string targetName,
            uint type,
            uint flags);

        // Prefixes for system-internal credentials we should skip
        private static readonly string[] SkipPrefixes = new[]
        {
            "MicrosoftAccount:",
            "WindowsLive:",
            "virtualapp/didlogical",
            "SSO_POP_Device",
            "LegacyGeneric:target=MicrosoftAccount:",
            "LegacyGeneric:target=WindowsLive:",
            "Microsoft_WinInet_",
        };

        /// <summary>
        /// Exports all relevant Credential Manager entries for the current user
        /// to a passphrase-encrypted JSON file in the backup directory.
        /// </summary>
        public static int ExportCredentials(string backupDir, string passphrase, Action<string>? log = null)
        {
            string exportPath = Path.Combine(backupDir, "CredentialManager.dat");
            var credentials = new List<ExportedCredential>();

            if (!CredEnumerateW(null, 0, out int count, out IntPtr credentialsPtr))
            {
                int error = Marshal.GetLastWin32Error();
                if (error == 1168) // ERROR_NOT_FOUND — no credentials stored
                {
                    log?.Invoke("Credential Manager: No stored credentials found.");
                    return 0;
                }
                log?.Invoke($"Credential Manager: CredEnumerateW failed with error {error}.");
                return 0;
            }

            try
            {
                int rdpCount = 0, driveCount = 0, genericCount = 0;

                for (int i = 0; i < count; i++)
                {
                    IntPtr credPtr = Marshal.ReadIntPtr(credentialsPtr, i * IntPtr.Size);
                    CREDENTIAL cred = Marshal.PtrToStructure<CREDENTIAL>(credPtr);

                    // Only export Generic and Domain Password types
                    if (cred.Type != CRED_TYPE_GENERIC && cred.Type != CRED_TYPE_DOMAIN_PASSWORD)
                        continue;

                    string target = cred.TargetName ?? string.Empty;

                    // Skip system-internal credentials
                    bool skip = false;
                    foreach (var prefix in SkipPrefixes)
                    {
                        if (target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            skip = true;
                            break;
                        }
                    }
                    if (skip) continue;

                    // Extract the password blob
                    string password = string.Empty;
                    if (cred.CredentialBlobSize > 0 && cred.CredentialBlob != IntPtr.Zero)
                    {
                        byte[] blob = new byte[cred.CredentialBlobSize];
                        Marshal.Copy(cred.CredentialBlob, blob, 0, blob.Length);
                        password = Encoding.Unicode.GetString(blob);
                    }

                    credentials.Add(new ExportedCredential
                    {
                        TargetName = target,
                        UserName = cred.UserName ?? string.Empty,
                        Password = password,
                        CredentialType = (int)cred.Type,
                        Persistence = (int)cred.Persist
                    });

                    // Categorize for logging
                    if (target.StartsWith("TERMSRV/", StringComparison.OrdinalIgnoreCase))
                        rdpCount++;
                    else if (target.StartsWith(@"\\") || target.Contains(@"\"))
                        driveCount++;
                    else
                        genericCount++;
                }

                if (credentials.Count == 0)
                {
                    log?.Invoke("Credential Manager: No exportable credentials found (system credentials filtered).");
                    return 0;
                }

                // Serialize to JSON, then encrypt with passphrase
                string json = JsonSerializer.Serialize(credentials, new JsonSerializerOptions { WriteIndented = true });
                byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
                PortableEncryption.EncryptToFile(jsonBytes, passphrase, exportPath);

                log?.Invoke($"Credential Manager: Exported {credentials.Count} credentials ({rdpCount} RDP, {driveCount} network, {genericCount} generic).");
                return credentials.Count;
            }
            finally
            {
                CredFree(credentialsPtr);
            }
        }

        /// <summary>
        /// Imports credentials from a passphrase-encrypted export file back into
        /// the current user's Credential Manager via CredWriteW.
        /// </summary>
        public static int ImportCredentials(string stagingDir, string passphrase, Action<string>? log = null)
        {
            string importPath = Path.Combine(stagingDir, "CredentialManager.dat");
            if (!File.Exists(importPath))
            {
                log?.Invoke("Credential Manager: No credential export file found — skipping import.");
                return 0;
            }

            try
            {
                byte[] jsonBytes = PortableEncryption.DecryptFromFile(importPath, passphrase);
                string json = Encoding.UTF8.GetString(jsonBytes);

                var credentials = JsonSerializer.Deserialize<List<ExportedCredential>>(json);
                if (credentials == null || credentials.Count == 0)
                {
                    log?.Invoke("Credential Manager: Export file was empty.");
                    return 0;
                }

                int imported = 0;
                int failed = 0;

                foreach (var cred in credentials)
                {
                    if (WriteCredential(cred))
                    {
                        imported++;
                        log?.Invoke($"  Restored: {cred.TargetName} ({cred.UserName})");
                    }
                    else
                    {
                        failed++;
                        log?.Invoke($"  Failed to restore: {cred.TargetName} (error {Marshal.GetLastWin32Error()})");
                    }
                }

                log?.Invoke($"Credential Manager: Imported {imported} credentials ({failed} failed).");
                return imported;
            }
            catch (CryptographicException)
            {
                log?.Invoke("Credential Manager: Incorrect passphrase — cannot decrypt credential file. Skipping.");
                return 0;
            }
            catch (Exception ex)
            {
                log?.Invoke($"Credential Manager: Import error — {ex.Message}");
                return 0;
            }
        }

        private static bool WriteCredential(ExportedCredential exportedCred)
        {
            byte[] passwordBytes = Encoding.Unicode.GetBytes(exportedCred.Password);
            IntPtr passwordPtr = Marshal.AllocHGlobal(passwordBytes.Length);

            try
            {
                Marshal.Copy(passwordBytes, 0, passwordPtr, passwordBytes.Length);

                var cred = new CREDENTIAL
                {
                    Flags = 0,
                    Type = (uint)exportedCred.CredentialType,
                    TargetName = exportedCred.TargetName,
                    UserName = exportedCred.UserName,
                    CredentialBlobSize = (uint)passwordBytes.Length,
                    CredentialBlob = passwordPtr,
                    Persist = exportedCred.Persistence > 0 ? (uint)exportedCred.Persistence : (uint)CRED_PERSIST_LOCAL_MACHINE,
                    Comment = "Restored by ProfileShift",
                    AttributeCount = 0,
                    Attributes = IntPtr.Zero,
                    TargetAlias = null!
                };

                return CredWriteW(ref cred, 0);
            }
            finally
            {
                Marshal.FreeHGlobal(passwordPtr);
            }
        }

        /// <summary>
        /// Returns a summary of what's currently in the user's Credential Manager
        /// (for UI display, not export — does not include password data).
        /// </summary>
        public static (int rdp, int network, int generic) GetCredentialSummary()
        {
            if (!CredEnumerateW(null, 0, out int count, out IntPtr credentialsPtr))
                return (0, 0, 0);

            try
            {
                int rdp = 0, network = 0, generic = 0;

                for (int i = 0; i < count; i++)
                {
                    IntPtr credPtr = Marshal.ReadIntPtr(credentialsPtr, i * IntPtr.Size);
                    CREDENTIAL cred = Marshal.PtrToStructure<CREDENTIAL>(credPtr);

                    if (cred.Type != CRED_TYPE_GENERIC && cred.Type != CRED_TYPE_DOMAIN_PASSWORD)
                        continue;

                    string target = cred.TargetName ?? string.Empty;

                    bool skip = false;
                    foreach (var prefix in SkipPrefixes)
                    {
                        if (target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        {
                            skip = true;
                            break;
                        }
                    }
                    if (skip) continue;

                    if (target.StartsWith("TERMSRV/", StringComparison.OrdinalIgnoreCase))
                        rdp++;
                    else if (target.StartsWith(@"\\") || target.Contains(@"\"))
                        network++;
                    else
                        generic++;
                }

                return (rdp, network, generic);
            }
            finally
            {
                CredFree(credentialsPtr);
            }
        }
    }
}
