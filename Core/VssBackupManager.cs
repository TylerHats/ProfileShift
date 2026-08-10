using System;
using System.Diagnostics;
using System.IO;

namespace ProfileShift.Core
{
    public static class VssBackupManager
    {
        public static bool CreateShadowCopy(string driveLetter, out string snapshotDevicePath, out string errorLog)
        {
            snapshotDevicePath = string.Empty;
            errorLog = string.Empty;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "vssadmin.exe",
                    Arguments = $"create shadow /for={driveLetter}:",
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var p = Process.Start(psi);
                if (p != null)
                {
                    string stdout = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(15000);

                    if (stdout.Contains("Shadow Copy Volume Name:"))
                    {
                        int idx = stdout.IndexOf(@"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy");
                        if (idx >= 0)
                        {
                            int end = stdout.IndexOfAny(new[] { '\r', '\n' }, idx);
                            snapshotDevicePath = end > idx ? stdout.Substring(idx, end - idx).Trim() : stdout.Substring(idx).Trim();
                            return true;
                        }
                    }

                    errorLog = stdout;
                }
            }
            catch (Exception ex)
            {
                errorLog = ex.Message;
            }

            return false;
        }

        public static void DeleteShadowCopy(string snapshotDevicePath)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "vssadmin.exe",
                    Arguments = "delete shadows /all /quiet",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using var p = Process.Start(psi);
                p?.WaitForExit(10000);
            }
            catch { }
        }
    }
}
