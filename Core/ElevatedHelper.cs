using System;
using System.Diagnostics;
using System.Security.Principal;

namespace ProfileShift.Core
{
    public static class ElevatedHelper
    {
        /// <summary>
        /// Returns true if the current process is running with administrator privileges.
        /// </summary>
        public static bool IsAlreadyElevated()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Spawns an elevated child process to run admin-requiring backup tasks
        /// (DISM app association export, WiFi profile export).
        /// Returns true if the elevated process completed successfully.
        /// Triggers a UAC prompt if the current process is not already elevated.
        /// </summary>
        public static bool RunElevatedBackupTasks(string backupDir, Action<string>? log = null)
        {
            if (IsAlreadyElevated())
            {
                // Already elevated — run tasks inline
                RunAdminBackupTasks(backupDir, log);
                return true;
            }

            try
            {
                string exePath = Process.GetCurrentProcess().MainModule?.FileName
                    ?? System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ProfileShift.exe");

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = $"--elevated-helper --backup-dir \"{backupDir}\"",
                    Verb = "runas",
                    UseShellExecute = true,
                    CreateNoWindow = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                log?.Invoke("Requesting administrator privileges for system-level exports...");

                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    log?.Invoke("Failed to start elevated helper process.");
                    return false;
                }

                proc.WaitForExit(60000); // 60 second timeout
                bool success = proc.ExitCode == 0;

                if (success)
                    log?.Invoke("Elevated tasks completed successfully.");
                else
                    log?.Invoke($"Elevated helper exited with code {proc.ExitCode}.");

                return success;
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED
            {
                log?.Invoke("UAC prompt was cancelled — skipping system-level exports (WiFi, App Associations).");
                return false;
            }
            catch (Exception ex)
            {
                log?.Invoke($"Error launching elevated helper: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Spawns an elevated child process for restore operations.
        /// Returns true if the elevated process completed successfully.
        /// </summary>
        public static bool RunElevatedRestoreTasks(string backupSourcePath, string selectedUsersArg, Action<string>? log = null)
        {
            if (IsAlreadyElevated())
            {
                // Already elevated — return true to signal the caller can proceed inline
                return true;
            }

            try
            {
                string exePath = Process.GetCurrentProcess().MainModule?.FileName
                    ?? System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ProfileShift.exe");

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = $"--elevated-restore --src \"{backupSourcePath}\" --users \"{selectedUsersArg}\"",
                    Verb = "runas",
                    UseShellExecute = true,
                    CreateNoWindow = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                log?.Invoke("Requesting administrator privileges for restore operations...");

                using var proc = Process.Start(psi);
                if (proc == null)
                {
                    log?.Invoke("Failed to start elevated restore process.");
                    return false;
                }

                proc.WaitForExit(); // No timeout for restore — can take a while
                bool success = proc.ExitCode == 0;

                if (success)
                    log?.Invoke("Elevated restore tasks completed successfully.");
                else
                    log?.Invoke($"Elevated restore process exited with code {proc.ExitCode}.");

                return success;
            }
            catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                log?.Invoke("UAC prompt was cancelled — restore requires administrator privileges.");
                return false;
            }
            catch (Exception ex)
            {
                log?.Invoke($"Error launching elevated restore: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Runs the admin-requiring backup tasks directly (called either inline
        /// when already elevated, or from the --elevated-helper CLI route).
        /// </summary>
        public static void RunAdminBackupTasks(string backupDir, Action<string>? log = null)
        {
            // 1. DISM — Export default app associations
            string appAssocXml = System.IO.Path.Combine(backupDir, "AppAssoc.xml");
            try
            {
                SettingsMigrator.ExportDefaultAppAssociations(appAssocXml);
                log?.Invoke("Default Application Associations exported.");
            }
            catch (Exception ex)
            {
                log?.Invoke($"DISM app association export failed: {ex.Message}");
            }

            // 2. WiFi profiles
            string wifiFolder = System.IO.Path.Combine(backupDir, "WiFi_Profiles");
            try
            {
                WifiProfileMigrator.ExportWifiProfiles(wifiFolder);
                log?.Invoke("Wi-Fi Network SSID Profiles exported.");
            }
            catch (Exception ex)
            {
                log?.Invoke($"WiFi profile export failed: {ex.Message}");
            }
        }

        /// <summary>
        /// CLI entry point for the elevated helper process.
        /// Called from Program.cs when --elevated-helper is passed.
        /// Returns 0 on success, 1 on failure.
        /// </summary>
        public static int RunElevatedHelperCli(string backupDir)
        {
            try
            {
                Console.WriteLine($"[ProfileShift Elevated Helper] Backup dir: {backupDir}");
                RunAdminBackupTasks(backupDir, msg => Console.WriteLine($"[ELEVATED] {msg}"));
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ELEVATED] Fatal error: {ex.Message}");
                return 1;
            }
        }
    }
}
