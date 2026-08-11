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

            // --- Credential Import (requires passphrase from backup) ---
            string credManagerFile = Path.Combine(publicStaging, "CredentialManager.dat");
            string browserPwFile = Path.Combine(publicStaging, "BrowserPasswords.dat");
            bool hasCredentialExports = File.Exists(credManagerFile) || File.Exists(browserPwFile);

            if (hasCredentialExports)
            {
                UpdateStatus("Credential exports detected — enter your backup passphrase to restore them.");

                // Prompt for the passphrase (this runs during logon, user is present)
                string passphrase = await Dispatcher.InvokeAsync(() =>
                {
                    return PromptForPassphrase(
                        "Credential Import Passphrase",
                        "This backup includes saved credentials (passwords, RDP, etc.).\n" +
                        "Enter the passphrase that was set during backup to import them.\n\n" +
                        "Click Cancel to skip credential import.");
                });

                if (!string.IsNullOrEmpty(passphrase))
                {
                    if (File.Exists(credManagerFile))
                    {
                        UpdateStatus("Restoring saved credentials (RDP, network drives, VPN)...");
                        await Task.Run(() =>
                        {
                            CredentialManagerExporter.ImportCredentials(publicStaging, passphrase,
                                msg => Dispatcher.Invoke(() => UpdateStatus(msg)));
                        });
                    }

                    if (File.Exists(browserPwFile))
                    {
                        UpdateStatus("Preparing browser password import...");
                        await Task.Run(() =>
                        {
                            BrowserPasswordExporter.ImportBrowserPasswords(publicStaging, passphrase,
                                msg => Dispatcher.Invoke(() => UpdateStatus(msg)));
                        });

                        UpdateStatus("Browser password CSVs ready — import them from the browser's password settings page.");
                        await Task.Delay(5000);
                    }
                }
                else
                {
                    UpdateStatus("Credential import skipped (no passphrase provided).");
                    await Task.Delay(2000);
                }
            }

            UpdateStatus("Generating Migration Summary report on Desktop...");
            ReportGenerator.SaveReportToDesktop(config, matchedUser);

            UpdateStatus("Finalizing profile integration...");
            await Task.Delay(3000);

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

            UpdateStatus("Integration complete! Logging off to apply deep settings...");
            await Task.Delay(3000);

            Process.Start("logoff.exe");
            Close();
        }

        private void UpdateStatus(string message)
        {
            Dispatcher.Invoke(() =>
            {
                StatusLabel.Text = message;
            });
        }

        private string PromptForPassphrase(string title, string message)
        {
            // Temporarily allow interaction past the topmost overlay
            this.Topmost = false;

            var dialog = new Window
            {
                Title = title,
                Width = 420,
                Height = 230,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                Topmost = true,
                Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#2F3136")),
                Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#D9D9D9")),
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                FontSize = 13
            };

            var stack = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };

            var msgBlock = new System.Windows.Controls.TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#D9D9D9")),
                Margin = new Thickness(0, 0, 0, 12)
            };
            stack.Children.Add(msgBlock);

            var pwBox = new System.Windows.Controls.PasswordBox
            {
                Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#40444B")),
                Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#D9D9D9")),
                BorderBrush = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#202225")),
                Padding = new Thickness(6),
                FontSize = 14,
                Height = 32,
                Margin = new Thickness(0, 0, 0, 16)
            };
            stack.Children.Add(pwBox);

            var btnPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var btnOk = new System.Windows.Controls.Button
            {
                Content = "OK",
                Width = 80,
                Height = 30,
                Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#7289DA")),
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true
            };
            btnOk.Click += (s, ev) => { dialog.DialogResult = true; dialog.Close(); };
            btnPanel.Children.Add(btnOk);

            var btnCancel = new System.Windows.Controls.Button
            {
                Content = "Skip",
                Width = 80,
                Height = 30,
                Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#40444B")),
                Foreground = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#D9D9D9")),
                IsCancel = true
            };
            btnPanel.Children.Add(btnCancel);

            stack.Children.Add(btnPanel);
            dialog.Content = stack;

            Utilities.DwmHelper.EnableDarkModeTitleBar(dialog);

            string result = string.Empty;
            if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(pwBox.Password))
            {
                result = pwBox.Password;
            }

            this.Topmost = true;
            return result;
        }
    }
}
