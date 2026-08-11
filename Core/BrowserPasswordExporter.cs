using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ProfileShift.Models;

namespace ProfileShift.Core
{
    public static class BrowserPasswordExporter
    {
        // --- Win32 DPAPI P/Invoke ---

        [StructLayout(LayoutKind.Sequential)]
        private struct DATA_BLOB
        {
            public int cbData;
            public IntPtr pbData;
        }

        [DllImport("crypt32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool CryptUnprotectData(
            ref DATA_BLOB pDataIn,
            IntPtr ppszDataDescr,
            IntPtr pOptionalEntropy,
            IntPtr pvReserved,
            IntPtr pPromptStruct,
            uint dwFlags,
            ref DATA_BLOB pDataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);

        /// <summary>
        /// Browser definitions — maps browser name to its User Data directory
        /// relative to the user profile path.
        /// </summary>
        private static readonly (string Name, string RelativeUserDataPath, string ExeName)[] ChromiumBrowsers = new[]
        {
            ("Google Chrome",   @"AppData\Local\Google\Chrome\User Data",               "chrome.exe"),
            ("Microsoft Edge",  @"AppData\Local\Microsoft\Edge\User Data",              "msedge.exe"),
            ("Brave Browser",   @"AppData\Local\BraveSoftware\Brave-Browser\User Data", "brave.exe"),
            ("Vivaldi",         @"AppData\Local\Vivaldi\User Data",                     "vivaldi.exe"),
            ("Opera",           @"AppData\Roaming\Opera Software\Opera Stable",         "opera.exe"),
        };

        // --- Native Extract Mode ---

        /// <summary>
        /// Exports browser passwords using native DPAPI decryption for all detected
        /// Chromium browser profiles. Saves results as a DPAPI-encrypted JSON file.
        /// Requires the browser processes to be closed.
        /// </summary>
        public static int ExportPasswordsNative(string userProfilePath, string backupDir, string passphrase, Action<string>? log = null)
        {
            var allPasswords = new List<BrowserPasswordEntry>();

            foreach (var browser in ChromiumBrowsers)
            {
                string userDataPath = Path.Combine(userProfilePath, browser.RelativeUserDataPath);
                if (!Directory.Exists(userDataPath))
                    continue;

                // Check if the browser is running
                string processName = Path.GetFileNameWithoutExtension(browser.ExeName);
                var runningProcesses = Process.GetProcessesByName(processName);
                if (runningProcesses.Length > 0)
                {
                    log?.Invoke($"  {browser.Name}: Browser is running — cannot access password database. Skipping.");
                    foreach (var p in runningProcesses) p.Dispose();
                    continue;
                }

                try
                {
                    // Read the master AES key from Local State
                    byte[]? masterKey = ExtractChromiumMasterKey(userDataPath);
                    if (masterKey == null)
                    {
                        log?.Invoke($"  {browser.Name}: Could not extract master key. Skipping.");
                        continue;
                    }

                    // Find all profile directories
                    var profiles = GetChromiumProfiles(userDataPath);
                    int browserTotal = 0;

                    foreach (var (profileDir, profileName) in profiles)
                    {
                        string loginDataPath = Path.Combine(profileDir, "Login Data");
                        if (!File.Exists(loginDataPath))
                            continue;

                        var passwords = ExtractPasswordsFromLoginData(loginDataPath, masterKey, browser.Name, profileName, log);
                        allPasswords.AddRange(passwords);
                        browserTotal += passwords.Count;
                    }

                    log?.Invoke($"  {browser.Name}: Exported {browserTotal} passwords from {profiles.Count} profile(s).");
                }
                catch (Exception ex)
                {
                    log?.Invoke($"  {browser.Name}: Error during export — {ex.Message}");
                }
            }

            if (allPasswords.Count == 0)
            {
                log?.Invoke("Browser Passwords: No passwords found to export.");
                return 0;
            }

            // Serialize and encrypt with passphrase
            string json = JsonSerializer.Serialize(allPasswords, new JsonSerializerOptions { WriteIndented = true });
            byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
            PortableEncryption.EncryptToFile(jsonBytes, passphrase, Path.Combine(backupDir, "BrowserPasswords.dat"));

            log?.Invoke($"Browser Passwords: Exported {allPasswords.Count} total passwords (encrypted).");
            return allPasswords.Count;
        }

        /// <summary>
        /// Reads the Chromium master AES key from the Local State file,
        /// then decrypts it using DPAPI CryptUnprotectData.
        /// </summary>
        private static byte[]? ExtractChromiumMasterKey(string userDataPath)
        {
            string localStatePath = Path.Combine(userDataPath, "Local State");
            if (!File.Exists(localStatePath))
                return null;

            string localStateJson = File.ReadAllText(localStatePath);
            using var doc = JsonDocument.Parse(localStateJson);

            if (!doc.RootElement.TryGetProperty("os_crypt", out var osCrypt))
                return null;
            if (!osCrypt.TryGetProperty("encrypted_key", out var encKeyElement))
                return null;

            string encKeyBase64 = encKeyElement.GetString() ?? string.Empty;
            if (string.IsNullOrEmpty(encKeyBase64))
                return null;

            byte[] encKeyBytes = Convert.FromBase64String(encKeyBase64);

            // Strip the "DPAPI" prefix (5 bytes)
            if (encKeyBytes.Length < 5)
                return null;

            byte[] dpapiBlob = new byte[encKeyBytes.Length - 5];
            Array.Copy(encKeyBytes, 5, dpapiBlob, 0, dpapiBlob.Length);

            // Decrypt with DPAPI
            return DpapiDecrypt(dpapiBlob);
        }

        /// <summary>
        /// Decrypts a DPAPI-protected byte array using CryptUnprotectData.
        /// Works only for the current user's data.
        /// </summary>
        private static byte[]? DpapiDecrypt(byte[] encryptedData)
        {
            var dataIn = new DATA_BLOB();
            var dataOut = new DATA_BLOB();

            IntPtr encPtr = Marshal.AllocHGlobal(encryptedData.Length);
            try
            {
                Marshal.Copy(encryptedData, 0, encPtr, encryptedData.Length);
                dataIn.pbData = encPtr;
                dataIn.cbData = encryptedData.Length;

                if (!CryptUnprotectData(ref dataIn, IntPtr.Zero, IntPtr.Zero,
                    IntPtr.Zero, IntPtr.Zero, 0, ref dataOut))
                {
                    return null;
                }

                byte[] result = new byte[dataOut.cbData];
                Marshal.Copy(dataOut.pbData, result, 0, dataOut.cbData);
                LocalFree(dataOut.pbData);
                return result;
            }
            finally
            {
                Marshal.FreeHGlobal(encPtr);
            }
        }

        /// <summary>
        /// Finds all Chrome/Edge profile subdirectories in the User Data folder.
        /// Returns tuples of (full path, display name).
        /// </summary>
        private static List<(string Path, string Name)> GetChromiumProfiles(string userDataPath)
        {
            var profiles = new List<(string, string)>();

            // Default profile
            string defaultProfile = Path.Combine(userDataPath, "Default");
            if (Directory.Exists(defaultProfile))
                profiles.Add((defaultProfile, "Default"));

            // Numbered profiles: "Profile 1", "Profile 2", etc.
            try
            {
                foreach (var dir in Directory.GetDirectories(userDataPath, "Profile *"))
                {
                    string dirName = Path.GetFileName(dir);
                    profiles.Add((dir, dirName));
                }
            }
            catch { }

            return profiles;
        }

        /// <summary>
        /// Reads and decrypts all password entries from a Chromium Login Data SQLite file.
        /// Copies the file to a temp location first to avoid locking issues.
        /// </summary>
        private static List<BrowserPasswordEntry> ExtractPasswordsFromLoginData(
            string loginDataPath, byte[] masterKey, string browserName, string profileName, Action<string>? log = null)
        {
            var passwords = new List<BrowserPasswordEntry>();

            // Copy Login Data to a temp file to avoid SQLite lock conflicts
            string tempCopy = Path.Combine(Path.GetTempPath(), $"ProfileShift_LoginData_{Guid.NewGuid():N}.db");

            try
            {
                File.Copy(loginDataPath, tempCopy, true);

                using var connection = new SqliteConnection($"Data Source={tempCopy};Mode=ReadOnly");
                connection.Open();

                using var cmd = connection.CreateCommand();
                cmd.CommandText = "SELECT origin_url, username_value, password_value FROM logins WHERE password_value IS NOT NULL AND length(password_value) > 0";

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string url = reader.GetString(0);
                    string username = reader.GetString(1);
                    byte[] encPassword = (byte[])reader.GetValue(2);

                    if (encPassword.Length == 0)
                        continue;

                    string? decrypted = DecryptChromiumPassword(encPassword, masterKey);
                    if (string.IsNullOrEmpty(decrypted))
                        continue;

                    passwords.Add(new BrowserPasswordEntry
                    {
                        Browser = browserName,
                        ProfileName = profileName,
                        Url = url,
                        Username = username,
                        Password = decrypted
                    });
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"    {browserName}/{profileName}: Error reading Login Data — {ex.Message}");
            }
            finally
            {
                try { File.Delete(tempCopy); } catch { }
            }

            return passwords;
        }

        /// <summary>
        /// Decrypts a Chromium password value using AES-256-GCM.
        /// Password blobs start with "v10" or "v20" (3-byte version prefix),
        /// followed by 12-byte nonce, then the AES-GCM ciphertext+tag.
        /// </summary>
        private static string? DecryptChromiumPassword(byte[] encPassword, byte[] masterKey)
        {
            try
            {
                // Check for v10/v20 prefix (indicates AES-GCM encryption with master key)
                if (encPassword.Length > 3 &&
                    encPassword[0] == 'v' &&
                    (encPassword[1] == '1' || encPassword[1] == '2') &&
                    encPassword[2] == '0')
                {
                    // v10/v20 format: [3-byte prefix][12-byte nonce][ciphertext][16-byte auth tag]
                    const int nonceLength = 12;
                    const int tagLength = 16;

                    byte[] nonce = new byte[nonceLength];
                    Array.Copy(encPassword, 3, nonce, 0, nonceLength);

                    int ciphertextLength = encPassword.Length - 3 - nonceLength - tagLength;
                    if (ciphertextLength <= 0)
                        return null;

                    byte[] ciphertext = new byte[ciphertextLength];
                    Array.Copy(encPassword, 3 + nonceLength, ciphertext, 0, ciphertextLength);

                    byte[] tag = new byte[tagLength];
                    Array.Copy(encPassword, encPassword.Length - tagLength, tag, 0, tagLength);

                    byte[] plaintext = new byte[ciphertextLength];

                    using var aesGcm = new AesGcm(masterKey, tagLength);
                    aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);

                    return Encoding.UTF8.GetString(plaintext);
                }
                else
                {
                    // Legacy DPAPI-only encryption (older Chrome versions)
                    byte[]? decrypted = DpapiDecrypt(encPassword);
                    return decrypted != null ? Encoding.UTF8.GetString(decrypted) : null;
                }
            }
            catch
            {
                return null;
            }
        }

        // --- Browser-Assisted Mode ---

        /// <summary>
        /// Opens each detected Chromium browser to its password manager page,
        /// one profile at a time, so the user can manually export passwords.
        /// Returns the list of browser/profile pairs that were opened.
        /// </summary>
        public static List<(string Browser, string Profile, string ExePath)> GetBrowserProfilesForAssistedExport(string userProfilePath)
        {
            var results = new List<(string, string, string)>();

            foreach (var browser in ChromiumBrowsers)
            {
                string userDataPath = Path.Combine(userProfilePath, browser.RelativeUserDataPath);
                if (!Directory.Exists(userDataPath))
                    continue;

                // Find the browser executable
                string? exePath = FindBrowserExecutable(browser.ExeName);
                if (exePath == null)
                    continue;

                var profiles = GetChromiumProfiles(userDataPath);
                foreach (var (_, profileName) in profiles)
                {
                    results.Add((browser.Name, profileName, exePath));
                }
            }

            return results;
        }

        /// <summary>
        /// Launches a specific browser profile to its password manager page.
        /// </summary>
        public static void OpenBrowserPasswordPage(string browserName, string profileName, string exePath)
        {
            string passwordUrl = browserName switch
            {
                "Google Chrome"  => "chrome://password-manager/passwords",
                "Microsoft Edge" => "edge://settings/passwords",
                "Brave Browser"  => "brave://settings/passwords",
                "Vivaldi"        => "vivaldi://settings/passwords",
                "Opera"          => "opera://settings/passwords",
                _                => "about:settings"
            };

            string args = $"--profile-directory=\"{profileName}\" \"{passwordUrl}\"";

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = args,
                    UseShellExecute = false
                });
            }
            catch { }
        }

        /// <summary>
        /// After browser-assisted export, scans common locations for exported CSV files
        /// and moves them into the backup directory with passphrase encryption.
        /// </summary>
        public static int CollectAssistedExportCSVs(string backupDir, string passphrase, Action<string>? log = null)
        {
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] searchDirs = new[]
            {
                Path.Combine(userProfile, "Downloads"),
                Path.Combine(userProfile, "Desktop"),
                userProfile
            };

            string[] csvPatterns = new[]
            {
                "*passwords*.csv",
                "*Passwords*.csv",
                "Chrome Passwords*.csv",
                "Edge Passwords*.csv",
                "Brave Passwords*.csv",
            };

            var collected = new List<string>();
            var cutoffTime = DateTime.Now.AddMinutes(-30); // Only files created recently

            foreach (var dir in searchDirs)
            {
                if (!Directory.Exists(dir)) continue;

                foreach (var pattern in csvPatterns)
                {
                    try
                    {
                        foreach (var file in Directory.GetFiles(dir, pattern))
                        {
                            var info = new FileInfo(file);
                            if (info.CreationTime > cutoffTime && !collected.Contains(file))
                            {
                                collected.Add(file);
                            }
                        }
                    }
                    catch { }
                }
            }

            if (collected.Count == 0)
            {
                log?.Invoke("Browser Passwords: No exported CSV files found in Downloads/Desktop.");
                return 0;
            }

            // Read all CSVs, encrypt with passphrase
            var allEntries = new List<BrowserPasswordEntry>();
            foreach (var csvPath in collected)
            {
                try
                {
                    var lines = File.ReadAllLines(csvPath);
                    // Standard Chromium CSV format: name,url,username,password,note
                    for (int i = 1; i < lines.Length; i++) // Skip header
                    {
                        var fields = ParseCsvLine(lines[i]);
                        if (fields.Length >= 4)
                        {
                            allEntries.Add(new BrowserPasswordEntry
                            {
                                Browser = GuessSourceBrowser(csvPath),
                                ProfileName = "Exported",
                                Url = fields[1],
                                Username = fields[2],
                                Password = fields[3]
                            });
                        }
                    }

                    // Delete the plaintext CSV after collecting
                    File.Delete(csvPath);
                    log?.Invoke($"  Collected and removed: {Path.GetFileName(csvPath)}");
                }
                catch (Exception ex)
                {
                    log?.Invoke($"  Error reading CSV {Path.GetFileName(csvPath)}: {ex.Message}");
                }
            }

            if (allEntries.Count > 0)
            {
                string json = JsonSerializer.Serialize(allEntries, new JsonSerializerOptions { WriteIndented = true });
                byte[] jsonBytes = Encoding.UTF8.GetBytes(json);
                PortableEncryption.EncryptToFile(jsonBytes, passphrase, Path.Combine(backupDir, "BrowserPasswords.dat"));

                log?.Invoke($"Browser Passwords: Collected {allEntries.Count} passwords from {collected.Count} CSV file(s).");
            }

            return allEntries.Count;
        }

        // --- Restore / Import ---

        /// <summary>
        /// For restore: writes per-browser CSV files from the encrypted export
        /// and opens each browser to its password import page.
        /// </summary>
        public static void ImportBrowserPasswords(string stagingDir, string passphrase, Action<string>? log = null)
        {
            string importPath = Path.Combine(stagingDir, "BrowserPasswords.dat");
            if (!File.Exists(importPath))
            {
                log?.Invoke("Browser Passwords: No export file found — skipping import.");
                return;
            }

            try
            {
                byte[] jsonBytes = PortableEncryption.DecryptFromFile(importPath, passphrase);
                string json = Encoding.UTF8.GetString(jsonBytes);

                var allEntries = JsonSerializer.Deserialize<List<BrowserPasswordEntry>>(json);
                if (allEntries == null || allEntries.Count == 0)
                {
                    log?.Invoke("Browser Passwords: Export file was empty.");
                    return;
                }

                // Group by browser and write per-browser CSVs for import
                var grouped = allEntries.GroupBy(e => e.Browser);
                string tempDir = Path.Combine(Path.GetTempPath(), "ProfileShift_PasswordImport");
                Directory.CreateDirectory(tempDir);

                foreach (var group in grouped)
                {
                    string csvPath = Path.Combine(tempDir, $"Passwords_{SanitizeFileName(group.Key)}.csv");
                    var sb = new StringBuilder();
                    sb.AppendLine("name,url,username,password,note");

                    foreach (var entry in group)
                    {
                        string name = new Uri(entry.Url).Host;
                        sb.AppendLine($"{EscapeCsv(name)},{EscapeCsv(entry.Url)},{EscapeCsv(entry.Username)},{EscapeCsv(entry.Password)},");
                    }

                    File.WriteAllText(csvPath, sb.ToString());
                    log?.Invoke($"  Created import CSV for {group.Key}: {group.Count()} passwords");

                    // Try to open the browser to its password import/settings page
                    string? exePath = FindBrowserExecutable(GetExeNameForBrowser(group.Key));
                    if (exePath != null)
                    {
                        string passwordUrl = group.Key switch
                        {
                            "Google Chrome"  => "chrome://settings/passwords",
                            "Microsoft Edge" => "edge://settings/passwords",
                            "Brave Browser"  => "brave://settings/passwords",
                            "Vivaldi"        => "vivaldi://settings/passwords",
                            "Opera"          => "opera://settings/passwords",
                            _                => "about:settings"
                        };

                        try
                        {
                            Process.Start(new ProcessStartInfo
                            {
                                FileName = exePath,
                                Arguments = $"\"{passwordUrl}\"",
                                UseShellExecute = false
                            });
                            log?.Invoke($"  Opened {group.Key} to password settings — import the CSV from: {csvPath}");
                        }
                        catch { }
                    }
                }

                log?.Invoke($"Browser Passwords: {allEntries.Count} passwords ready for import. CSVs saved to: {tempDir}");
            }
            catch (CryptographicException)
            {
                log?.Invoke("Browser Passwords: Incorrect passphrase — cannot decrypt export file. Skipping.");
            }
            catch (Exception ex)
            {
                log?.Invoke($"Browser Passwords: Import error — {ex.Message}");
            }
        }

        // --- Utility Methods ---

        /// <summary>
        /// Returns a list of detected Chromium browsers with password data,
        /// for UI display purposes.
        /// </summary>
        public static List<string> GetDetectedBrowsersWithPasswords(string userProfilePath)
        {
            var detected = new List<string>();
            foreach (var browser in ChromiumBrowsers)
            {
                string userDataPath = Path.Combine(userProfilePath, browser.RelativeUserDataPath);
                if (!Directory.Exists(userDataPath))
                    continue;

                var profiles = GetChromiumProfiles(userDataPath);
                bool hasLoginData = profiles.Any(p => File.Exists(Path.Combine(p.Path, "Login Data")));
                if (hasLoginData)
                    detected.Add(browser.Name);
            }
            return detected;
        }

        private static string? FindBrowserExecutable(string exeName)
        {
            // Check common installation paths
            string[] searchPaths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Google", "Chrome", "Application"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Google", "Chrome", "Application"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "BraveSoftware", "Brave-Browser", "Application"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "BraveSoftware", "Brave-Browser", "Application"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vivaldi", "Application"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Opera"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Opera"),
            };

            foreach (var searchPath in searchPaths)
            {
                string fullPath = Path.Combine(searchPath, exeName);
                if (File.Exists(fullPath))
                    return fullPath;

                // Also check subdirectories (Chrome/Edge version folders)
                if (Directory.Exists(searchPath))
                {
                    try
                    {
                        foreach (var dir in Directory.GetDirectories(searchPath))
                        {
                            fullPath = Path.Combine(dir, exeName);
                            if (File.Exists(fullPath))
                                return fullPath;
                        }
                    }
                    catch { }
                }
            }

            return null;
        }

        private static string GetExeNameForBrowser(string browserName)
        {
            return browserName switch
            {
                "Google Chrome"  => "chrome.exe",
                "Microsoft Edge" => "msedge.exe",
                "Brave Browser"  => "brave.exe",
                "Vivaldi"        => "vivaldi.exe",
                "Opera"          => "opera.exe",
                _                => "chrome.exe"
            };
        }

        private static string GuessSourceBrowser(string csvPath)
        {
            string fileName = Path.GetFileName(csvPath).ToLowerInvariant();
            if (fileName.Contains("chrome")) return "Google Chrome";
            if (fileName.Contains("edge")) return "Microsoft Edge";
            if (fileName.Contains("brave")) return "Brave Browser";
            if (fileName.Contains("vivaldi")) return "Vivaldi";
            if (fileName.Contains("opera")) return "Opera";
            return "Unknown Browser";
        }

        private static string SanitizeFileName(string name)
        {
            return string.Concat(name.Split(Path.GetInvalidFileNameChars()));
        }

        private static string EscapeCsv(string value)
        {
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
                return $"\"{value.Replace("\"", "\"\"")}\"";
            return value;
        }

        /// <summary>
        /// Simple CSV line parser that handles quoted fields.
        /// </summary>
        private static string[] ParseCsvLine(string line)
        {
            var fields = new List<string>();
            bool inQuotes = false;
            var current = new StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            current.Append('"');
                            i++; // Skip escaped quote
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
                else
                {
                    if (c == '"')
                    {
                        inQuotes = true;
                    }
                    else if (c == ',')
                    {
                        fields.Add(current.ToString());
                        current.Clear();
                    }
                    else
                    {
                        current.Append(c);
                    }
                }
            }

            fields.Add(current.ToString());
            return fields.ToArray();
        }
    }
}
