using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using ProfileShift.Utilities;

namespace ProfileShift.UI.Views
{
    public class LogEntry
    {
        public string Timestamp { get; set; } = string.Empty;
        public string Level { get; set; } = "Info";
        public string Message { get; set; } = string.Empty;
    }

    public partial class LogViewerModal : Window
    {
        private List<LogEntry> _allEntries = new List<LogEntry>();

        public LogViewerModal(IEnumerable<string> rawLogLines)
        {
            InitializeComponent();
            Loaded += LogViewerModal_Loaded;
            ParseLogs(rawLogLines);
        }

        private void LogViewerModal_Loaded(object sender, RoutedEventArgs e)
        {
            DwmHelper.EnableDarkModeTitleBar(this);
            ApplyFilters();
        }

        private void ParseLogs(IEnumerable<string> rawLogLines)
        {
            _allEntries.Clear();
            foreach (var line in rawLogLines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                string level = "Info";
                if (line.Contains("[Warning]", StringComparison.OrdinalIgnoreCase)) level = "Warning";
                else if (line.Contains("[Error]", StringComparison.OrdinalIgnoreCase)) level = "Error";

                _allEntries.Add(new LogEntry
                {
                    Timestamp = DateTime.Now.ToString("HH:mm:ss"),
                    Level = level,
                    Message = line
                });
            }
        }

        private void ApplyFilters()
        {
            string query = TxtSearch.Text?.ToLowerInvariant() ?? "";
            bool showInfo = ChkInfo.IsChecked == true;
            bool showWarn = ChkWarning.IsChecked == true;
            bool showErr = ChkError.IsChecked == true;

            var filtered = _allEntries.Where(e =>
            {
                if (e.Level == "Info" && !showInfo) return false;
                if (e.Level == "Warning" && !showWarn) return false;
                if (e.Level == "Error" && !showErr) return false;

                if (!string.IsNullOrEmpty(query) && !e.Message.ToLowerInvariant().Contains(query))
                    return false;

                return true;
            }).ToList();

            DgLogs.ItemsSource = filtered;
        }

        private void TxtSearch_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            ApplyFilters();
        }

        private void Filter_Click(object sender, RoutedEventArgs e)
        {
            ApplyFilters();
        }

        private void BtnExportTxt_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                FileName = "ProfileShift_Log.txt"
            };

            if (dialog.ShowDialog() == true)
            {
                var lines = _allEntries.Select(entry => $"[{entry.Timestamp}] [{entry.Level}] {entry.Message}");
                File.WriteAllLines(dialog.FileName, lines);
                MessageBox.Show("Logs exported successfully!", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
