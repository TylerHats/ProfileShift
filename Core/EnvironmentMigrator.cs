using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ProfileShift.Core
{
    public static class EnvironmentMigrator
    {
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, UIntPtr wParam, string lParam, uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);

        private const uint WM_SETTINGCHANGE = 0x001A;
        private const uint SMTO_ABORTIFHUNG = 0x0002;

        public static Dictionary<string, string> ExtractUserEnvironmentVariables()
        {
            var envVars = new Dictionary<string, string>();
            try
            {
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey("Environment"))
                {
                    if (key != null)
                    {
                        foreach (string valueName in key.GetValueNames())
                        {
                            string? valueData = key.GetValue(valueName)?.ToString();
                            if (!string.IsNullOrEmpty(valueData))
                            {
                                envVars[valueName] = valueData;
                            }
                        }
                    }
                }
            }
            catch { }

            return envVars;
        }

        public static void ApplyUserEnvironmentVariables(Dictionary<string, string> envVars)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey("Environment"))
                {
                    foreach (var kvp in envVars)
                    {
                        key.SetValue(kvp.Key, kvp.Value);
                    }
                }

                // Broadcast WM_SETTINGCHANGE to notify running applications of environment change
                UIntPtr result;
                SendMessageTimeout((IntPtr)0xffff, WM_SETTINGCHANGE, UIntPtr.Zero, "Environment", SMTO_ABORTIFHUNG, 2000, out result);
            }
            catch { }
        }
    }
}
