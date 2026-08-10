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

        private CancellationTokenSource? _estimateCts;

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
            await UpdateLiveEstimateAsync();

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

            if (ChkRootData?.IsChecked == true)
            {
                foldersToScan.AddRange(FolderScanner.GetRootDriveFolders());
            }

            foreach (var user in selectedUsers)
            {
                if (ChkUserProfiles?.IsChecked == true)
                {
                    foldersToScan.AddRange(FolderScanner.GetUserSelectableFolders(user.ProfilePath));
                }
            }

            try
            {
                var (bytes, count) = await FolderScanner.CalculateTotalStatsAsync(foldersToScan, token);
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
                    }
                }
            }
        }

        private void BtnCustomizeFolders_Click(object sender, RoutedEventArgs e)
        {
            var selectedUsers = _userProfiles.Where(u => u.IsSelected).ToList();
            var allFolders = new List<string>();

            if (ChkRootData.IsChecked == true)
            {
                allFolders.AddRange(FolderScanner.GetRootDriveFolders());
            }

            foreach (var user in selectedUsers)
            {
                allFolders.AddRange(FolderScanner.GetUserSelectableFolders(user.ProfilePath));
            }

            var modal = new Views.FolderPickerModal(allFolders)
            {
                Owner = this
            };

            if (modal.ShowDialog() == true)
            {
                Log($"Custom folder selection updated: {modal.SelectedFolderPaths.Count} subfolders selected.");
            }
        }

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

            BtnStartBackup.IsEnabled = false;
            BtnCancelBackup.IsEnabled = true;
            _cts = new CancellationTokenSource();

            var rootFolders = ChkRootData.IsChecked == true ? FolderScanner.GetRootDriveFolders() : new List<string>();
            var userFoldersMap = new Dictionary<string, List<string>>();
            var userBrowsersMap = new Dictionary<string, List<string>>();

            foreach (var user in selectedUsers)
            {
                if (ChkUserProfiles.IsChecked == true)
                {
                    userFoldersMap[user.Username] = FolderScanner.GetUserSelectableFolders(user.ProfilePath);
                }

                if (ChkBrowsers.IsChecked == true)
                {
                    var available = BrowserDetector.GetAvailableBrowsers(user.ProfilePath);
                    userBrowsersMap[user.Username] = available.Where(b => b.IsInstalled).Select(b => b.RelativePath).ToList();
                }
            }

            bool success = await _backupEngine.RunBackupAsync(dest, selectedUsers, rootFolders, userFoldersMap, userBrowsersMap, _cts.Token);

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

            BtnStartRestore.IsEnabled = false;
            bool success = await _restoreEngine.StageRestoreAsync(src, selectedUsers);
            BtnStartRestore.IsEnabled = true;

            if (success)
            {
                MessageBox.Show("Profile staging completed successfully! Log off and log in as the migrated user to finalize profile setup.", "Staging Ready", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

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
    }
}
