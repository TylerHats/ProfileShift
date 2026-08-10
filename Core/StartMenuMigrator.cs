using System;
using System.IO;

namespace ProfileShift.Core
{
    public static class StartMenuMigrator
    {
        public static void BackupStartMenuPins(string userProfilePath, string backupDestinationDir)
        {
            try
            {
                // Windows 10 Start Menu Layout
                string win10Path = Path.Combine(userProfilePath, @"AppData\Local\Microsoft\Windows\Shell\LayoutModification.xml");
                if (File.Exists(win10Path))
                {
                    File.Copy(win10Path, Path.Combine(backupDestinationDir, "LayoutModification.xml"), true);
                }

                // Windows 11 Start Menu Pins (start.bin)
                string win11Path = Path.Combine(userProfilePath, @"AppData\Local\Packages\Microsoft.Windows.StartMenuExperienceHost_cw5n1h2txyewy\LocalState\start.bin");
                if (File.Exists(win11Path))
                {
                    File.Copy(win11Path, Path.Combine(backupDestinationDir, "start.bin"), true);
                }
            }
            catch { }
        }

        public static void RestoreStartMenuPins(string targetUserProfilePath, string backupSourceDir)
        {
            try
            {
                // Restore Windows 10 layout
                string srcWin10 = Path.Combine(backupSourceDir, "LayoutModification.xml");
                if (File.Exists(srcWin10))
                {
                    string targetWin10Dir = Path.Combine(targetUserProfilePath, @"AppData\Local\Microsoft\Windows\Shell");
                    Directory.CreateDirectory(targetWin10Dir);
                    File.Copy(srcWin10, Path.Combine(targetWin10Dir, "LayoutModification.xml"), true);
                }

                // Restore Windows 11 start.bin
                string srcWin11 = Path.Combine(backupSourceDir, "start.bin");
                if (File.Exists(srcWin11))
                {
                    string targetWin11Dir = Path.Combine(targetUserProfilePath, @"AppData\Local\Packages\Microsoft.Windows.StartMenuExperienceHost_cw5n1h2txyewy\LocalState");
                    Directory.CreateDirectory(targetWin11Dir);
                    File.Copy(srcWin11, Path.Combine(targetWin11Dir, "start.bin"), true);
                }
            }
            catch { }
        }
    }
}
