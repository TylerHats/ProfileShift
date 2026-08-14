using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using ProfileShift.Core;
using ProfileShift.Models;
using ProfileShift.Utilities;

namespace ProfileShift.UI
{
    public partial class MainWindow : Window
    {
        private List<UserProfile> _userProfiles = new List<UserProfile>();
        private CancellationTokenSource? _cts;
        private BackupEngine _backupEngine = new BackupEngine();
        private RestoreEngine _restoreEngine = new RestoreEngine();

        private Dictionary<string, List<string>>? _customUserFolders;
        private Dictionary<string, List<string>>? _customUserExcludedFolders;
        private List<string>? _customRootFolders;
        private List<string>? _customExcludedRootFolders;

        private CancellationTokenSource? _estimateCts;
        private bool _isAlreadyElevated;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;

            _backupEngine.LogMessage += (s, msg) => Log(msg);
            _backupEngine.ProgressChanged += (s, e) => Dispatcher.Invoke(() =>
            {
                TxtStatus.Text = e.CurrentStatus;
                MainProgressBar.Value = e.Percentage;
                if (e.SpeedMbps > 0)
                {
                    TxtTelemetry.Text = $"Speed: {e.SpeedMbps:N2} MB/s | ETA: {e.EstimatedTimeRemaining:hh\\:mm\\:ss}";
                }
            });

            _restoreEngine.LogMessage += (s, msg) => Log(msg);
            _restoreEngine.ProgressChanged += (s, pct) => Dispatcher.Invoke(() =>
            {
                MainProgressBar.Value = pct;
            });
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            DwmHelper.EnableDarkModeTitleBar(this);
            LoadUserProfiles();

            if (ChkRootData != null)
            {
                ChkRootData.Checked += async (s, ev) => await UpdateLiveEstimateAsync();
                ChkRootData.Unchecked += async (s, ev) => await UpdateLiveEstimateAsync();
            }

            if (ChkUserProfiles != null)
            {
                ChkUserProfiles.Checked += async (s, ev) => await UpdateLiveEstimateAsync();
                ChkUserProfiles.Unchecked += async (s, ev) => await UpdateLiveEstimateAsync();
            }

            if (ChkBrowsers != null)
            {
                ChkBrowsers.Checked += async (s, ev) => await UpdateLiveEstimateAsync();
                ChkBrowsers.Unchecked += async (s, ev) => await UpdateLiveEstimateAsync();
            }

            if (TxtBackupPath != null)
            {
                TxtBackupPath.TextChanged += async (s, ev) => await UpdateLiveEstimateAsync();
            }

            await UpdateLiveEstimateAsync();

            // Check elevation state on startup
            _isAlreadyElevated = ElevatedHelper.IsAlreadyElevated();
            UpdateUacShieldVisibility();

            var updateInfo = await UpdateChecker.CheckForUpdatesAsync();
            if (updateInfo != null && updateInfo.IsNewer)
            {
                Log($"Update Available: {updateInfo.TagName}. Download at {updateInfo.HtmlUrl}");
            }
        }

        private async System.Threading.Tasks.Task UpdateLiveEstimateAsync()
        {
            _estimateCts?.Cancel();
            _estimateCts = new CancellationTokenSource();
            var token = _estimateCts.Token;

            LblLiveEstimate.Text = "Selected: Calculating...";

            var selectedUsers = _userProfiles.Where(u => u.IsSelected).ToList();
            var foldersToScan = new List<string>();
            var allExclusions = new List<string>();

            if (ChkRootData?.IsChecked == true)
            {
                var roots = _customRootFolders ?? FolderScanner.GetRootDriveFolders(TxtBackupPath?.Text);
                foldersToScan.AddRange(roots);
                if (_customExcludedRootFolders != null)
                {
                    allExclusions.AddRange(_customExcludedRootFolders);
                }
            }

            foreach (var user in selectedUsers)
            {
                if (ChkUserProfiles?.IsChecked == true)
                {
                    var uFolders = _customUserFolders != null && _customUserFolders.TryGetValue(user.Username, out var cf)
                        ? cf
                        : FolderScanner.GetUserSelectableFolders(user.ProfilePath);
                    foldersToScan.AddRange(uFolders);

                    if (_customUserExcludedFolders != null && _customUserExcludedFolders.TryGetValue(user.Username, out var ce))
                    {
                        allExclusions.AddRange(ce);
                    }
                }

                if (ChkBrowsers?.IsChecked == true)
                {
                    var available = BrowserDetector.GetAvailableBrowsers(user.ProfilePath);
                    foreach (var b in available.Where(b => b.IsInstalled))
                    {
                        string fullBPath = Path.Combine(user.ProfilePath, b.RelativePath);
                        if (Directory.Exists(fullBPath))
                        {
                            foldersToScan.Add(fullBPath);
                        }
                    }
                }
            }

            string destPath = TxtBackupPath?.Text ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(destPath))
            {
                allExclusions.Add(destPath);
            }

            try
            {
                var (bytes, count) = await FolderScanner.CalculateTotalStatsAsync(foldersToScan, allExclusions, token);
                if (!token.IsCancellationRequested)
                {
                    double gb = Math.Round(bytes / 1073741824.0, 2);
                    LblLiveEstimate.Text = $"Selected: {count:N0} files ({gb} GB)";
                }
            }
            catch
            {
                LblLiveEstimate.Text = "Selected: Calculation Error";
            }
        }

        private void LoadUserProfiles()
        {
            _userProfiles = UserDetection.GetLocalUserProfiles();
            foreach (var up in _userProfiles)
            {
                up.PropertyChanged += async (s, e) =>
                {
                    if (e.PropertyName == nameof(UserProfile.IsSelected))
                    {
                        await UpdateLiveEstimateAsync();
                    }
                };
            }
            LstUsers.ItemsSource = _userProfiles;
        }

        private void BtnBackupTab_Click(object sender, RoutedEventArgs e)
        {
            PanelBackup.Visibility = Visibility.Visible;
            PanelRestore.Visibility = Visibility.Collapsed;
            BtnBackupTab.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#36393F"));
            BtnBackupTab.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D9D9D9"));
            BtnRestoreTab.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2F3136"));
            BtnRestoreTab.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#808080"));
        }

        private void BtnRestoreTab_Click(object sender, RoutedEventArgs e)
        {
            PanelRestore.Visibility = Visibility.Visible;
            PanelBackup.Visibility = Visibility.Collapsed;
            BtnRestoreTab.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#36393F"));
            BtnRestoreTab.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D9D9D9"));
            BtnBackupTab.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2F3136"));
            BtnBackupTab.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#808080"));
        }

        private void BtnBrowseBackup_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog();
            if (dialog.ShowDialog() == true)
            {
                TxtBackupPath.Text = dialog.FolderName;
            }
        }

        private void BtnBrowseRestore_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog();
            if (dialog.ShowDialog() == true)
            {
                TxtRestorePath.Text = dialog.FolderName;
            }
        }

        private void TxtRestorePath_TextChanged(object sender, TextChangedEventArgs e)
        {
            string path = TxtRestorePath.Text;
            if (Directory.Exists(path))
            {
                string jsonConfig = Path.Combine(path, "Migration.json");
                string yamlConfig = Path.Combine(path, "Migration.yaml");
                string cfg = File.Exists(jsonConfig) ? jsonConfig : yamlConfig;

                if (File.Exists(cfg))
                {
                    MigrationConfig? config = ConfigManager.LoadAutoConfig(cfg);
                    if (config != null)
                    {
                        LstRestoreUsers.ItemsSource = config.SelectedUsers;

                        // Show credential info if available
                        var credInfo = new List<string>();
                        if (File.Exists(Path.Combine(path, "CredentialManager.dat")))
                            credInfo.Add("✓ Credential Manager data");
                        if (File.Exists(Path.Combine(path, "BrowserPasswords.dat")))
                            credInfo.Add("✓ Browser passwords");

                        if (credInfo.Count > 0)
                        {
                            LblRestoreCredentialInfo.Text = "This backup includes: " + string.Join(", ", credInfo);
                            LblRestoreCredentialInfo.Visibility = Visibility.Visible;
                        }
                        else
                        {
                            LblRestoreCredentialInfo.Visibility = Visibility.Collapsed;
                        }
                    }
                }
            }
        }

        private async void BtnCustomizeFolders_Click(object sender, RoutedEventArgs e)
        {
            var selectedUsers = _userProfiles.Where(u => u.IsSelected).ToList();
            if (selectedUsers.Count == 0)
            {
                MessageBox.Show("Please select at least one user profile first.", "No Users Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            bool includeRoot = ChkRootData.IsChecked == true;

            var modal = new Views.FolderPickerModal(
                selectedUsers,
                includeRoot,
                _customUserFolders,
                _customUserExcludedFolders,
                _customRootFolders,
                _customExcludedRootFolders)
            {
                Owner = this
            };

            if (modal.ShowDialog() == true)
            {
                _customUserFolders = new Dictionary<string, List<string>>(modal.UserFoldersMap);
                _customUserExcludedFolders = new Dictionary<string, List<string>>(modal.UserExcludedFoldersMap);
                _customRootFolders = new List<string>(modal.SelectedRootFolders);
                _customExcludedRootFolders = new List<string>(modal.ExcludedRootFolders);

                int totalIncluded = _customUserFolders.Values.Sum(v => v.Count) + _customRootFolders.Count;
                int totalExcluded = _customUserExcludedFolders.Values.Sum(v => v.Count) + _customExcludedRootFolders.Count;

                Log($"Custom folder selection updated: {totalIncluded} top-level folders included, {totalExcluded} subfolders excluded.");
                await UpdateLiveEstimateAsync();
            }
        }

        // --- UAC Shield Logic ---

        private void ElevationOption_Changed(object sender, RoutedEventArgs e)
        {
            UpdateUacShieldVisibility();
        }

        private void CredentialOption_Changed(object sender, RoutedEventArgs e)
        {
            UpdateUacShieldVisibility();

            // Show/hide password warning banner
            if (PasswordWarningBanner != null)
            {
                bool showWarning = ChkBrowserPasswords?.IsChecked == true || ChkCredentialManager?.IsChecked == true;
                PasswordWarningBanner.Visibility = showWarning ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void UpdateUacShieldVisibility()
        {
            if (UacShieldBackup == null || UacShieldRestore == null) return;

            if (_isAlreadyElevated)
            {
                // Already running elevated — no shield needed on either button
                UacShieldBackup.Visibility = Visibility.Collapsed;
                UacShieldRestore.Visibility = Visibility.Collapsed;
                return;
            }

            // Backup: shield needed if any admin-requiring option is checked
            // (Settings/DISM, WiFi profiles are always exported, so Settings checkbox controls this)
            bool backupNeedsAdmin = ChkSettings?.IsChecked == true;
            UacShieldBackup.Visibility = backupNeedsAdmin ? Visibility.Visible : Visibility.Collapsed;

            // Restore: always needs admin (staging folder, schtasks, icacls)
            UacShieldRestore.Visibility = Visibility.Visible;
        }

        // --- Backup ---

        private async void BtnStartBackup_Click(object sender, RoutedEventArgs e)
        {
            string dest = TxtBackupPath.Text;
            if (string.IsNullOrWhiteSpace(dest) || !Directory.Exists(dest))
            {
                MessageBox.Show("Please select a valid destination folder.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selectedUsers = _userProfiles.Where(u => u.IsSelected).ToList();
            if (selectedUsers.Count == 0)
            {
                MessageBox.Show("Please select at least one user profile.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Determine browser password export mode if enabled
            string browserPasswordMode = "native";
            bool exportBrowserPasswords = ChkBrowserPasswords?.IsChecked == true;

            if (exportBrowserPasswords)
            {
                var modeResult = MessageBox.Show(
                    "How would you like to export browser passwords?\n\n" +
                    "YES — Automated (Native)\n" +
                    "Reads browser password databases directly. Faster and fully automated, " +
                    "but may trigger behavior-based antivirus alerts on some endpoint protection systems.\n\n" +
                    "NO — Browser-Assisted (Manual)\n" +
                    "Opens each browser to its password manager page so you can export manually. " +
                    "Zero antivirus risk, but requires you to click Export in each browser profile.\n\n" +
                    "CANCEL — Skip browser password export",
                    "Browser Password Export Mode",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (modeResult == MessageBoxResult.Cancel)
                {
                    exportBrowserPasswords = false;
                }
                else if (modeResult == MessageBoxResult.No)
                {
                    browserPasswordMode = "browser-assisted";
                }
                // Yes = "native" (default)
            }

            // If browser-assisted mode, launch browsers first and wait for user
            if (exportBrowserPasswords && browserPasswordMode == "browser-assisted")
            {
                string currentUserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var browserProfiles = BrowserPasswordExporter.GetBrowserProfilesForAssistedExport(currentUserProfile);

                if (browserProfiles.Count > 0)
                {
                    foreach (var (browser, profile, exePath) in browserProfiles)
                    {
                        Log($"Opening {browser} ({profile}) to password manager page...");
                        BrowserPasswordExporter.OpenBrowserPasswordPage(browser, profile, exePath);
                    }

                    MessageBox.Show(
                        $"ProfileShift has opened {browserProfiles.Count} browser profile(s) to their password manager pages.\n\n" +
                        "For each browser:\n" +
                        "1. Click the ⋮ (three dots) menu near \"Saved Passwords\"\n" +
                        "2. Click \"Export passwords\"\n" +
                        "3. Confirm with your Windows PIN/password\n" +
                        "4. Save the CSV to your Downloads folder\n\n" +
                        "Click OK when you have finished exporting from all browsers.",
                        "Export Browser Passwords",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }

            BtnStartBackup.IsEnabled = false;
            BtnCancelBackup.IsEnabled = true;
            _cts = new CancellationTokenSource();

            var rootFolders = ChkRootData.IsChecked == true
                ? (_customRootFolders ?? FolderScanner.GetRootDriveFolders(dest))
                : new List<string>();

            var userFoldersMap = new Dictionary<string, List<string>>();
            var userBrowsersMap = new Dictionary<string, List<string>>();

            foreach (var user in selectedUsers)
            {
                if (ChkUserProfiles.IsChecked == true)
                {
                    userFoldersMap[user.Username] = _customUserFolders != null && _customUserFolders.TryGetValue(user.Username, out var uFolders)
                        ? uFolders
                        : FolderScanner.GetUserSelectableFolders(user.ProfilePath);
                }

                if (ChkBrowsers.IsChecked == true)
                {
                    var available = BrowserDetector.GetAvailableBrowsers(user.ProfilePath);
                    userBrowsersMap[user.Username] = available.Where(b => b.IsInstalled).Select(b => b.RelativePath).ToList();
                }
            }

            bool exportCredentialManager = ChkCredentialManager?.IsChecked == true;
            bool needsPassphrase = exportCredentialManager || exportBrowserPasswords;
            string credentialPassphrase = string.Empty;

            if (needsPassphrase)
            {
                // Prompt for a passphrase to encrypt the credential exports
                credentialPassphrase = PromptForPassphrase(
                    "Set Credential Export Passphrase",
                    "Enter a passphrase to encrypt your exported credentials.\n" +
                    "You will need this same passphrase when restoring on the target machine.\n\n" +
                    "This protects your passwords in the backup folder.");

                if (string.IsNullOrEmpty(credentialPassphrase))
                {
                    Log("Credential export cancelled — no passphrase provided.");
                    exportCredentialManager = false;
                    exportBrowserPasswords = false;
                }
            }

            bool success = await _backupEngine.RunBackupAsync(
                dest, selectedUsers, rootFolders, userFoldersMap, userBrowsersMap, _cts.Token,
                exportCredentialManager: exportCredentialManager,
                exportBrowserPasswords: exportBrowserPasswords,
                browserPasswordMode: browserPasswordMode,
                credentialPassphrase: credentialPassphrase,
                userExcludedFoldersMap: _customUserExcludedFolders,
                excludedRootFolders: _customExcludedRootFolders);

            // If browser-assisted mode, collect the CSVs after backup
            if (success && exportBrowserPasswords && browserPasswordMode == "browser-assisted")
            {
                // Find the backup directory that was just created
                var latestBackup = Directory.GetDirectories(dest, "ProfileShift_Backup_*")
                    .OrderByDescending(d => d)
                    .FirstOrDefault();

                if (latestBackup != null)
                {
                    Log("Collecting exported browser password CSVs...");
                    BrowserPasswordExporter.CollectAssistedExportCSVs(latestBackup, credentialPassphrase, msg => Log(msg));
                }
            }

            BtnStartBackup.IsEnabled = true;
            BtnCancelBackup.IsEnabled = false;

            if (success)
            {
                MessageBox.Show("Backup completed successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnCancelBackup_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            Log("Cancelling operation...");
        }

        // --- Restore ---

        private async void BtnStartRestore_Click(object sender, RoutedEventArgs e)
        {
            string src = TxtRestorePath.Text;
            if (string.IsNullOrWhiteSpace(src) || !Directory.Exists(src))
            {
                MessageBox.Show("Please select a valid backup source folder.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selectedUsers = LstRestoreUsers.ItemsSource as List<string> ?? new List<string>();
            if (selectedUsers.Count == 0)
            {
                MessageBox.Show("No users available to restore.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Restore always requires elevation for staging folder + schtasks
            if (!_isAlreadyElevated)
            {
                string usersArg = string.Join("|", selectedUsers);
                bool elevated = ElevatedHelper.RunElevatedRestoreTasks(src, usersArg, msg => Log(msg));

                if (!elevated)
                {
                    MessageBox.Show("Restore requires administrator privileges. Please approve the UAC prompt.", "Elevation Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            // Check if credential exports exist in the backup source
            string credManagerFile = Path.Combine(src, "CredentialManager.dat");
            string browserPwFile = Path.Combine(src, "BrowserPasswords.dat");
            bool hasCredentials = File.Exists(credManagerFile) || File.Exists(browserPwFile);
            string passphrase = string.Empty;

            if (hasCredentials)
            {
                passphrase = PromptForPassphrase(
                    "Credential Import Passphrase",
                    "Saved credentials (passwords, RDP, etc.) were found in this backup.\n" +
                    "Enter the passphrase set during backup to restore credentials for the current user.\n\n" +
                    "If skipped, profile data will be restored without passwords.");

                if (!string.IsNullOrEmpty(passphrase))
                {
                    string currentUsername = Environment.UserName;
                    bool currentUserIsTarget = selectedUsers.Any(u => u.Equals(currentUsername, StringComparison.OrdinalIgnoreCase));

                    if (currentUserIsTarget)
                    {
                        Log("Restoring credentials for current user session...");
                        if (File.Exists(credManagerFile))
                        {
                            CredentialManagerExporter.ImportCredentials(src, passphrase, msg => Log(msg));
                        }

                        if (File.Exists(browserPwFile))
                        {
                            BrowserPasswordExporter.ImportBrowserPasswords(src, passphrase, msg => Log(msg));
                        }
                    }
                    else
                    {
                        Log("Note: Credentials are only restorable to the active user session. Staging remaining profile data for target user(s).");
                    }
                }
                else
                {
                    Log("Credential import skipped — no passphrase provided.");
                }
            }

            BtnStartRestore.IsEnabled = false;
            bool success = await _restoreEngine.StageRestoreAsync(src, selectedUsers);
            BtnStartRestore.IsEnabled = true;

            if (success)
            {
                MessageBox.Show("Profile staging completed successfully! When target user(s) log in for the first time, profile integration will run automatically.", "Staging Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // --- Logs ---

        private void BtnViewLogs_Click(object sender, RoutedEventArgs e)
        {
            var lines = TxtLog.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var modal = new Views.LogViewerModal(lines)
            {
                Owner = this
            };
            modal.ShowDialog();
        }

        private void Log(string message)
        {
            Dispatcher.Invoke(() =>
            {
                string timestamp = DateTime.Now.ToString("HH:mm:ss");
                TxtLog.AppendText($"[{timestamp}] {message}\n");
                TxtLog.ScrollToEnd();
            });
        }

        /// <summary>
        /// Shows a passphrase input dialog. Returns the entered passphrase,
        /// or empty string if cancelled.
        /// </summary>
        private string PromptForPassphrase(string title, string message)
        {
            var dialog = new Window
            {
                Title = title,
                Width = 420,
                Height = 230,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2F3136")),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D9D9D9")),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 13
            };

            var stack = new StackPanel { Margin = new Thickness(16) };

            var msgBlock = new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D9D9D9")),
                Margin = new Thickness(0, 0, 0, 12)
            };
            stack.Children.Add(msgBlock);

            var pwBox = new PasswordBox
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#40444B")),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D9D9D9")),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#202225")),
                Padding = new Thickness(6),
                FontSize = 14,
                Height = 32,
                Margin = new Thickness(0, 0, 0, 16)
            };
            stack.Children.Add(pwBox);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };

            var btnOk = new Button
            {
                Content = "OK",
                Width = 80,
                Height = 30,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#7289DA")),
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true
            };
            btnOk.Click += (s, ev) => { dialog.DialogResult = true; dialog.Close(); };
            btnPanel.Children.Add(btnOk);

            var btnCancel = new Button
            {
                Content = "Cancel",
                Width = 80,
                Height = 30,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#40444B")),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D9D9D9")),
                IsCancel = true
            };
            btnPanel.Children.Add(btnCancel);

            stack.Children.Add(btnPanel);
            dialog.Content = stack;

            Utilities.DwmHelper.EnableDarkModeTitleBar(dialog);

            if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(pwBox.Password))
            {
                return pwBox.Password;
            }
            return string.Empty;
        }
    }
}
