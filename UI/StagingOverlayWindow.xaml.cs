using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using ProfileShift.Core;
using ProfileShift.Models;

namespace ProfileShift.UI
{
    public partial class StagingOverlayWindow : Window
    {
        public StagingOverlayWindow()
        {
            InitializeComponent();
            Loaded += StagingOverlayWindow_Loaded;
        }

        private async void StagingOverlayWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await RunStagingProcessAsync();
        }

        private async Task RunStagingProcessAsync()
        {
            string publicStaging = @"C:\System_ProfileShift_Staging";
            string jsonConfigPath = Path.Combine(publicStaging, "Migration.json");
            string yamlConfigPath = Path.Combine(publicStaging, "Migration.yaml");

            string configPath = File.Exists(jsonConfigPath) ? jsonConfigPath : yamlConfigPath;

            if (!File.Exists(configPath))
            {
                UpdateStatus("No migration configuration found. Closing...");
                await Task.Delay(2000);
                Close();
                return;
            }

            MigrationConfig? config = ConfigManager.LoadAutoConfig(configPath);
            if (config == null)
            {
                UpdateStatus("Failed to parse migration configuration.");
                await Task.Delay(2000);
                Close();
                return;
            }

            string currentUsername = Environment.UserName;
            string matchedUser = string.Empty;

            foreach (var user in config.SelectedUsers)
            {
                if (currentUsername.Equals(user, StringComparison.OrdinalIgnoreCase))
                {
                    matchedUser = user;
                    break;
                }
            }

            if (string.IsNullOrEmpty(matchedUser))
            {
                UpdateStatus("Profile integration complete for other users.");
                await Task.Delay(2000);
                Close();
                return;
            }

            UpdateStatus("Copying files and documents...");
            string srcPath = Path.Combine(publicStaging, "StagedFiles", matchedUser);
            string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (Directory.Exists(srcPath))
            {
                await Task.Run(() =>
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "robocopy",
                        Arguments = $"\"{srcPath}\" \"{userProfile}\" /E /MOVE /IS /IT /MT:16 /NFL /NDL /NJH /NJS /nc /ns /np",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    using var p = Process.Start(psi);
                    p?.WaitForExit();
                });
            }

            UpdateStatus("Applying personalization, taskbar, and mapped drive settings...");
            if (config.UserSelections.TryGetValue(matchedUser, out var uSelection))
            {
                SettingsMigrator.ApplyUserSettings(uSelection.Settings);
                if (uSelection.EnvironmentVariables != null && uSelection.EnvironmentVariables.Count > 0)
                {
                    EnvironmentMigrator.ApplyUserEnvironmentVariables(uSelection.EnvironmentVariables);
                }
            }

            StartMenuMigrator.RestoreStartMenuPins(userProfile, publicStaging);

            string appAssocXml = Path.Combine(publicStaging, "AppAssoc.xml");
            if (File.Exists(appAssocXml))
            {
                UpdateStatus("Restoring default application associations...");
                SettingsMigrator.ImportDefaultAppAssociations(appAssocXml);
            }

            UpdateStatus("Generating Migration Summary report on Desktop...");
            ReportGenerator.SaveReportToDesktop(config, matchedUser);

            UpdateStatus("Finalizing profile integration...");
            await Task.Delay(1500);

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "schtasks.exe",
                    Arguments = "/delete /tn \"ProfileShiftSetupIntegration\" /f",
                    CreateNoWindow = true,
                    UseShellExecute = false
                })?.WaitForExit();

                if (Directory.Exists(publicStaging))
                {
                    Directory.Delete(publicStaging, true);
                }
            }
            catch { }

            UpdateStatus("Profile setup complete! Refreshing desktop...");
            await Task.Delay(1000);

            // Instantly apply wallpaper, theme, taskbar pins & file associations without logging off
            ShellRefreshHelper.RefreshShell();

            Close();
        }

        private void UpdateStatus(string message)
        {
            Dispatcher.Invoke(() =>
            {
                StatusLabel.Text = message;
            });
        }
    }
}
