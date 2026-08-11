using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using Microsoft.Win32;

namespace ProfileShift.Core
{
    public static class ShellRefreshHelper
    {
        [DllImport("shell32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern bool SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr hWnd, uint Msg, IntPtr wParam, string lParam,
            uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        private const int SHCNE_ASSOCCHANGED = 0x08000000;
        private const uint SHCNF_FLUSH = 0x1000;

        private const int SPI_SETDESKWALLPAPER = 0x0014;
        private const int SPIF_UPDATEINIFILE = 0x01;
        private const int SPIF_SENDCHANGE = 0x02;

        private static readonly IntPtr HWND_BROADCAST = new IntPtr(0xffff);
        private const uint WM_SETTINGCHANGE = 0x001A;
        private const uint SMTO_ABORTIFHUNG = 0x0002;

        /// <summary>
        /// Applies desktop wallpaper, notifies Windows shell of association/setting changes,
        /// and restarts explorer.exe so taskbar pins, theme colors, and desktop icons update instantly.
        /// </summary>
        public static void RefreshShell(string? wallpaperPath = null)
        {
            try
            {
                // 1. Update Desktop Wallpaper if path provided or in registry
                if (string.IsNullOrEmpty(wallpaperPath))
                {
                    using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
                    wallpaperPath = key?.GetValue("Wallpaper")?.ToString();
                }

                if (!string.IsNullOrEmpty(wallpaperPath) && System.IO.File.Exists(wallpaperPath))
                {
                    SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, wallpaperPath, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
                }

                // 2. Notify Shell of File Associations and System Setting changes
                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_FLUSH, IntPtr.Zero, IntPtr.Zero);
                SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero, "Environment", SMTO_ABORTIFHUNG, 2000, out _);
                SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero, "Policy", SMTO_ABORTIFHUNG, 2000, out _);
                SendMessageTimeout(HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero, "Personalize", SMTO_ABORTIFHUNG, 2000, out _);

                // 3. Restart Explorer process to instantly apply taskbar pins and shell settings
                RestartExplorer();
            }
            catch { }
        }

        private static void RestartExplorer()
        {
            try
            {
                var processes = Process.GetProcessesByName("explorer");
                foreach (var p in processes)
                {
                    try { p.Kill(); p.WaitForExit(3000); } catch { }
                    p.Dispose();
                }

                // Give Windows a brief pause to restart Explorer automatically or start it
                Thread.Sleep(500);

                var remaining = Process.GetProcessesByName("explorer");
                if (remaining.Length == 0)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        UseShellExecute = true
                    });
                }
                foreach (var r in remaining) r.Dispose();
            }
            catch { }
        }
    }
}
