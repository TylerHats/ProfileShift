using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ProfileShift.Models;

namespace ProfileShift.Core
{
    public class BackupProgressEventArgs : EventArgs
    {
        public string CurrentStatus { get; set; } = string.Empty;
        public int Percentage { get; set; }
        public long BytesCopied { get; set; }
        public long TotalBytes { get; set; }
        public double SpeedMbps { get; set; }
        public TimeSpan ElapsedTime { get; set; }
        public TimeSpan EstimatedTimeRemaining { get; set; }
        public string CurrentFileName { get; set; } = string.Empty;
    }

    public class BackupEngine
    {
        public event EventHandler<BackupProgressEventArgs>? ProgressChanged;
        public event EventHandler<string>? LogMessage;

        public async Task<bool> RunBackupAsync(
            string destinationFolder,
            List<UserProfile> selectedUsers,
            List<string> rootFolders,
            Dictionary<string, List<string>> userFoldersMap,
            Dictionary<string, List<string>> userBrowsersMap,
            CancellationToken cancellationToken,
            string configFormat = "json",
            bool exportCredentialManager = false,
            bool exportBrowserPasswords = false,
            string browserPasswordMode = "native",
            string credentialPassphrase = "",
            Dictionary<string, List<string>>? userExcludedFoldersMap = null,
            List<string>? excludedRootFolders = null)
        {
            string timeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string backupDir = Path.Combine(destinationFolder, $"ProfileShift_Backup_{timeStamp}");
            string cDriveDir = Path.Combine(backupDir, "C_Drive");
            Directory.CreateDirectory(cDriveDir);

            OnLog("Initializing backup destination: " + backupDir);

            var config = new MigrationConfig
            {
                OSBuild = Environment.OSVersion.Version.ToString(),
                SourceMachineName = Environment.MachineName,
                SourceDomain = Environment.UserDomainName,
                MigrationTime = DateTime.Now,
                SelectedRootFolders = rootFolders,
                ExcludedRootFolders = excludedRootFolders ?? new List<string>()
            };

            var foldersToCopy = new List<string>(rootFolders);

            // Collect all excluded folders across users and root
            var allExcludedFolders = new List<string>();
            if (excludedRootFolders != null)
            {
                allExcludedFolders.AddRange(excludedRootFolders);
            }

            foreach (var user in selectedUsers)
            {
                if (!user.IsSelected) continue;
                config.SelectedUsers.Add(user.Username);

                var uSelection = new UserSelection
                {
                    Settings = SettingsMigrator.ExtractUserSettings(user.Username),
                    EnvironmentVariables = EnvironmentMigrator.ExtractUserEnvironmentVariables()
                };

                StartMenuMigrator.BackupStartMenuPins(user.ProfilePath, backupDir);

                if (userFoldersMap.TryGetValue(user.Username, out var uFolders))
                {
                    uSelection.Folders = uFolders;
                    foldersToCopy.AddRange(uFolders);
                }

                if (userExcludedFoldersMap != null && userExcludedFoldersMap.TryGetValue(user.Username, out var uExcluded))
                {
                    uSelection.ExcludedFolders = uExcluded;
                    allExcludedFolders.AddRange(uExcluded);
                }

                if (userBrowsersMap.TryGetValue(user.Username, out var uBrowsers))
                {
                    uSelection.Browsers = uBrowsers;
                    foreach (var bPath in uBrowsers)
                    {
                        string fullBPath = Path.Combine(user.ProfilePath, bPath);
                        if (Directory.Exists(fullBPath))
                        {
                            foldersToCopy.Add(fullBPath);
                        }
                    }
                }

                config.UserSelections[user.Username] = uSelection;
                config.UserSoftware[user.Username] = SettingsMigrator.ExtractInstalledSoftware();
            }

            config.Printers = SettingsMigrator.ExtractPrintersDeduplicated();
            config.SystemSoftware = SettingsMigrator.ExtractInstalledSoftware();

            // --- Credential Manager Export (runs in current user context, no elevation) ---
            if (exportCredentialManager)
            {
                OnLog("Exporting Windows Credential Manager entries...");
                OnProgress("Exporting Credential Manager...", 2, 0, 0);
                int credCount = CredentialManagerExporter.ExportCredentials(backupDir, credentialPassphrase, msg => OnLog(msg));

                // Mark in config for each selected user (current user only)
                string currentUser = Environment.UserName;
                if (config.UserSelections.ContainsKey(currentUser))
                {
                    config.UserSelections[currentUser].CredentialManagerExported = credCount > 0;
                }
            }

            // --- Browser Password Export (runs in current user context, no elevation) ---
            if (exportBrowserPasswords)
            {
                string currentUserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string currentUser = Environment.UserName;

                if (browserPasswordMode == "native")
                {
                    OnLog("Exporting browser passwords (native mode)...");
                    OnProgress("Exporting browser passwords...", 4, 0, 0);
                    int pwCount = BrowserPasswordExporter.ExportPasswordsNative(currentUserProfile, backupDir, credentialPassphrase, msg => OnLog(msg));

                    if (config.UserSelections.ContainsKey(currentUser))
                    {
                        config.UserSelections[currentUser].BrowserPasswordsExported = pwCount > 0;
                        config.UserSelections[currentUser].BrowserPasswordExportMode = "native";
                    }
                }
                else if (browserPasswordMode == "browser-assisted")
                {
                    // Browser-assisted mode: CSVs are collected separately after the user exports them
                    // The caller (MainWindow) handles the interactive flow and calls CollectAssistedExportCSVs
                    OnLog("Browser-assisted password export: CSVs will be collected after manual export.");
                    if (config.UserSelections.ContainsKey(currentUser))
                    {
                        config.UserSelections[currentUser].BrowserPasswordExportMode = "browser-assisted";
                    }
                }
            }

            // --- Admin-requiring tasks: DISM app associations + WiFi profiles ---
            // These are delegated to an elevated helper process if we're not already elevated
            OnLog("Running system-level exports (may require administrator privileges)...");
            OnProgress("Running elevated system exports...", 6, 0, 0);
            ElevatedHelper.RunElevatedBackupTasks(backupDir, msg => OnLog(msg));

            // --- Calculate total backup size with exclusions ---
            OnLog("Calculating backup total size...");
            long totalBytes = FolderScanner.CalculateTotalSize(foldersToCopy, allExcludedFolders);
            OnLog($"Total estimated backup size: {Math.Round(totalBytes / 1073741824.0, 2)} GB");

            var spaceCheck = PreFlightChecker.CheckDestinationSpace(destinationFolder, totalBytes);
            OnLog(spaceCheck.WarningMessage);
            if (!spaceCheck.IsValid)
            {
                OnLog("Backup halted: Insufficient destination space.");
                return false;
            }

            // --- Save config files ---
            string jsonConfigPath = Path.Combine(backupDir, "Migration.json");
            ConfigManager.SaveConfigJson(config, jsonConfigPath);

            string yamlConfigPath = Path.Combine(backupDir, "Migration.yaml");
            ConfigManager.SaveConfigYaml(config, yamlConfigPath);

            OnLog("Config files Migration.json & Migration.yaml saved.");

            // --- File copy phase ---
            long copiedBytes = 0;
            int count = 0;

            foreach (var folder in foldersToCopy)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    OnLog("Backup operation cancelled by user.");
                    return false;
                }

                count++;
                int percent = 10 + (int)((double)count / foldersToCopy.Count * 90);
                OnProgress($"Copying ({count}/{foldersToCopy.Count}): {Path.GetFileName(folder)}", percent, copiedBytes, totalBytes);

                string driveRoot = Path.GetPathRoot(folder) ?? @"C:\";
                string relativePath = folder.StartsWith(driveRoot, StringComparison.OrdinalIgnoreCase)
                    ? folder.Substring(driveRoot.Length)
                    : Path.GetFileName(folder);

                string targetPath = Path.Combine(cDriveDir, relativePath);

                await CopyDirectoryRobocopyAsync(folder, targetPath, destinationFolder, backupDir, allExcludedFolders, cancellationToken);
            }

            OnLog("Backup completed successfully!");
            OnProgress("Backup Complete!", 100, totalBytes, totalBytes);
            return true;
        }

        private Task CopyDirectoryRobocopyAsync(
            string sourceDir,
            string targetDir,
            string destinationFolder,
            string backupDir,
            List<string>? specificExcludedDirs,
            CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                try
                {
                    Directory.CreateDirectory(targetDir);

                    var xdList = new List<string>
                    {
                        "\"OneDrive*\"",
                        "\"SharePoint*\"",
                        "\"Dropbox*\"",
                        "\"node_modules\"",
                        "\".git\"",
                        "\"Cache\"",
                        "\"GPUCache\"",
                        "\"Crashpad\"",
                        "\"Code Cache\"",
                        "\"ProfileShift_Backup*\"",
                        "\"System_ProfileShift_Staging*\"",
                        $"\"{backupDir}\""
                    };

                    if (!string.IsNullOrWhiteSpace(destinationFolder))
                    {
                        xdList.Add($"\"{destinationFolder.TrimEnd('\\')}\"");
                    }

                    if (specificExcludedDirs != null)
                    {
                        foreach (var excl in specificExcludedDirs)
                        {
                            if (!string.IsNullOrWhiteSpace(excl))
                            {
                                xdList.Add($"\"{excl.TrimEnd('\\')}\"");
                            }
                        }
                    }

                    string xdArg = string.Join(" ", xdList.Distinct(StringComparer.OrdinalIgnoreCase));
                    string args = $"\"{sourceDir}\" \"{targetDir}\" /E /COPY:DAT /DCOPY:DAT /XJ /R:1 /W:2 /MT:16 /XD {xdArg} /XF *.tmp *.bak *.iso *.vhd *.vhdx *.sys *.dmp desktop.ini Thumbs.db /NFL /NDL /NJH /NJS /nc /ns /np";

                    var psi = new ProcessStartInfo
                    {
                        FileName = "robocopy",
                        Arguments = args,
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };

                    using var proc = Process.Start(psi);
                    while (proc != null && !proc.HasExited)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            try { proc.Kill(); } catch { }
                            break;
                        }
                        Thread.Sleep(200);
                    }
                }
                catch (Exception ex)
                {
                    OnLog($"Error copying directory {sourceDir}: {ex.Message}");
                }
            }, cancellationToken);
        }

        private void OnLog(string message) => LogMessage?.Invoke(this, message);
        private void OnProgress(string status, int pct, long copied, long total) =>
            ProgressChanged?.Invoke(this, new BackupProgressEventArgs
            {
                CurrentStatus = status,
                Percentage = pct,
                BytesCopied = copied,
                TotalBytes = total
            });
    }
}

