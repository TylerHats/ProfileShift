using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using UserMoveTool.Models;

namespace UserMoveTool.Core
{
    public static class SettingsMigrator
    {
        public static UserSettings ExtractUserSettings(string username)
        {
            var settings = new UserSettings();
            string subKeyThemes = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
            string subKeyTaskbar = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
            string subKeySearch = @"Software\Microsoft\Windows\CurrentVersion\Search";
            string subKeyWallpaper = @"Control Panel\Desktop";
            string subKeyNetwork = @"Network";

            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(subKeyThemes))
                {
                    if (key != null)
                    {
                        settings.AppsUseLightTheme = key.GetValue("AppsUseLightTheme") as int?;
                        settings.SystemUsesLightTheme = key.GetValue("SystemUsesLightTheme") as int?;
                        settings.ColorPrevalence = key.GetValue("ColorPrevalence") as int?;
                    }
                }

                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(subKeyTaskbar))
                {
                    if (key != null)
                    {
                        settings.TaskbarAl = key.GetValue("TaskbarAl") as int?;
                        settings.ShowTaskViewButton = key.GetValue("ShowTaskViewButton") as int?;
                        settings.TaskbarDa = key.GetValue("TaskbarDa") as int?;
                    }
                }

                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(subKeySearch))
                {
                    if (key != null)
                    {
                        settings.SearchboxTaskbarMode = key.GetValue("SearchboxTaskbarMode") as int?;
                    }
                }

                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(subKeyWallpaper))
                {
                    if (key != null)
                    {
                        settings.WallpaperPath = key.GetValue("Wallpaper")?.ToString() ?? string.Empty;
                    }
                }

                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(subKeyNetwork))
                {
                    if (key != null)
                    {
                        foreach (string subName in key.GetSubKeyNames())
                        {
                            using (RegistryKey? driveKey = key.OpenSubKey(subName))
                            {
                                string? remotePath = driveKey?.GetValue("RemotePath")?.ToString();
                                if (!string.IsNullOrEmpty(remotePath))
                                {
                                    settings.MappedDrives.Add(new DriveMapInfo
                                    {
                                        DriveLetter = subName,
                                        RemotePath = remotePath
                                    });
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            return settings;
        }

        public static List<PrinterInfo> ExtractPrintersDeduplicated()
        {
            var printers = new List<PrinterInfo>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using (var searcher = new System.Management.ManagementObjectSearcher("SELECT * FROM Win32_Printer"))
                {
                    foreach (System.Management.ManagementObject printer in searcher.Get())
                    {
                        string name = printer["Name"]?.ToString() ?? string.Empty;
                        string driverName = printer["DriverName"]?.ToString() ?? string.Empty;
                        string portName = printer["PortName"]?.ToString() ?? string.Empty;
                        bool isShared = Convert.ToBoolean(printer["Shared"] ?? false);

                        if (string.IsNullOrWhiteSpace(name)) continue;

                        // Exclude virtual system printers
                        if (name.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("OneNote", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("PDF", StringComparison.OrdinalIgnoreCase) ||
                            name.Contains("XPS", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        if (!seenNames.Contains(name))
                        {
                            seenNames.Add(name);
                            printers.Add(new PrinterInfo
                            {
                                Name = name,
                                DriverName = driverName,
                                PortName = portName,
                                Shared = isShared
                            });
                        }
                    }
                }
            }
            catch { }

            return printers;
        }

        public static List<string> ExtractInstalledSoftware()
        {
            var software = new List<string>();
            string[] uninstallKeys = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            foreach (var keyPath in uninstallKeys)
            {
                try
                {
                    using (RegistryKey? hklmKey = Registry.LocalMachine.OpenSubKey(keyPath))
                    {
                        if (hklmKey != null)
                        {
                            foreach (string subkeyName in hklmKey.GetSubKeyNames())
                            {
                                using (RegistryKey? subkey = hklmKey.OpenSubKey(subkeyName))
                                {
                                    string? name = subkey?.GetValue("DisplayName")?.ToString();
                                    if (!string.IsNullOrWhiteSpace(name) && !software.Contains(name))
                                    {
                                        software.Add(name);
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            return software;
        }

        public static void ApplyUserSettings(UserSettings settings)
        {
            string subKeyThemes = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
            string subKeyTaskbar = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
            string subKeySearch = @"Software\Microsoft\Windows\CurrentVersion\Search";

            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(subKeyThemes))
                {
                    if (settings.AppsUseLightTheme.HasValue) key.SetValue("AppsUseLightTheme", settings.AppsUseLightTheme.Value);
                    if (settings.SystemUsesLightTheme.HasValue) key.SetValue("SystemUsesLightTheme", settings.SystemUsesLightTheme.Value);
                    if (settings.ColorPrevalence.HasValue) key.SetValue("ColorPrevalence", settings.ColorPrevalence.Value);
                }

                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(subKeyTaskbar))
                {
                    if (settings.TaskbarAl.HasValue) key.SetValue("TaskbarAl", settings.TaskbarAl.Value);
                    if (settings.ShowTaskViewButton.HasValue) key.SetValue("ShowTaskViewButton", settings.ShowTaskViewButton.Value);
                    if (settings.TaskbarDa.HasValue) key.SetValue("TaskbarDa", settings.TaskbarDa.Value);
                }

                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(subKeySearch))
                {
                    if (settings.SearchboxTaskbarMode.HasValue) key.SetValue("SearchboxTaskbarMode", settings.SearchboxTaskbarMode.Value);
                }

                foreach (var drive in settings.MappedDrives)
                {
                    string cmd = $"/c net use {drive.DriveLetter}: \"{drive.RemotePath}\" /persistent:yes";
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = cmd,
                        CreateNoWindow = true,
                        UseShellExecute = false
                    })?.WaitForExit(3000);
                }
            }
            catch { }
        }
    }
}
