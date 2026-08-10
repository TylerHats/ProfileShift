using System;
using System.Diagnostics;
using System.IO;

namespace ProfileShift.Core
{
    public static class WifiProfileMigrator
    {
        public static void ExportWifiProfiles(string outputFolder)
        {
            try
            {
                if (!Directory.Exists(outputFolder))
                {
                    Directory.CreateDirectory(outputFolder);
                }

                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c netsh wlan export profile folder=\"{outputFolder}\" key=clear",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                using var p = Process.Start(psi);
                p?.WaitForExit(5000);
            }
            catch { }
        }

        public static void RestoreWifiProfiles(string wifiProfilesFolder)
        {
            try
            {
                if (!Directory.Exists(wifiProfilesFolder)) return;

                var xmlFiles = Directory.GetFiles(wifiProfilesFolder, "*.xml");
                foreach (var xmlFile in xmlFiles)
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c netsh wlan add profile filename=\"{xmlFile}\" user=all",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };

                    using var p = Process.Start(psi);
                    p?.WaitForExit(3000);
                }
            }
            catch { }
        }
    }
}
