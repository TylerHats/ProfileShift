using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
            string configFormat = "json")
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
                SelectedRootFolders = rootFolders
            };

            var foldersToCopy = new List<string>(rootFolders);

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

            OnLog("Calculating backup total size...");
            long totalBytes = FolderScanner.CalculateTotalSize(foldersToCopy);
            OnLog($"Total estimated backup size: {Math.Round(totalBytes / 1073741824.0, 2)} GB");

            var spaceCheck = PreFlightChecker.CheckDestinationSpace(destinationFolder, totalBytes);
            OnLog(spaceCheck.WarningMessage);
            if (!spaceCheck.IsValid)
            {
                OnLog("Backup halted: Insufficient destination space.");
                return false;
            }

            string appAssocXml = Path.Combine(backupDir, "AppAssoc.xml");
            SettingsMigrator.ExportDefaultAppAssociations(appAssocXml);
            OnLog("Default Application Associations exported.");

            string jsonConfigPath = Path.Combine(backupDir, "Migration.json");
            ConfigManager.SaveConfigJson(config, jsonConfigPath);

            string yamlConfigPath = Path.Combine(backupDir, "Migration.yaml");
            ConfigManager.SaveConfigYaml(config, yamlConfigPath);

            OnLog("Config files Migration.json & Migration.yaml saved.");

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
                int percent = (int)((double)count / foldersToCopy.Count * 100);
                OnProgress($"Copying ({count}/{foldersToCopy.Count}): {Path.GetFileName(folder)}", percent, copiedBytes, totalBytes);

                if (!Directory.Exists(folder)) continue;

                string relativePath = folder.Length >= 3 ? folder.Substring(3) : Path.GetFileName(folder);
                string targetPath = Path.Combine(cDriveDir, relativePath);

                await CopyDirectoryRobocopyAsync(folder, targetPath, cancellationToken);
            }

            OnLog("Backup completed successfully!");
            OnProgress("Backup Complete!", 100, totalBytes, totalBytes);
            return true;
        }

        private Task CopyDirectoryRobocopyAsync(string sourceDir, string targetDir, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                try
                {
                    Directory.CreateDirectory(targetDir);
                    string args = $"\"{sourceDir}\" \"{targetDir}\" /E /COPY:DAT /DCOPY:DAT /R:1 /W:2 /MT:16 /XD \"OneDrive*\" \"SharePoint*\" \"Dropbox*\" /NFL /NDL /NJH /NJS /nc /ns /np";
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
