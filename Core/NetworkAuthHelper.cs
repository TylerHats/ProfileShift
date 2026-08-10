using System;
using System.Runtime.InteropServices;

namespace ProfileShift.Core
{
    public static class NetworkAuthHelper
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct NETRESOURCE
        {
            public uint dwScope;
            public uint dwType;
            public uint dwDisplayType;
            public uint dwUsage;
            public string lpLocalName;
            public string lpRemoteName;
            public string lpComment;
            public string lpProvider;
        }

        [DllImport("mpr.dll", CharSet = CharSet.Auto)]
        private static extern int WNetAddConnection2(ref NETRESOURCE lpNetResource, string lpPassword, string lpUsername, uint dwFlags);

        [DllImport("mpr.dll", CharSet = CharSet.Auto)]
        private static extern int WNetCancelConnection2(string lpName, uint dwFlags, bool fForce);

        public static bool ConnectToShare(string remoteUncPath, string username, string password, out string errorMessage)
        {
            errorMessage = string.Empty;
            var nr = new NETRESOURCE
            {
                dwType = 1, // RESOURCETYPE_DISK
                lpRemoteName = remoteUncPath
            };

            int result = WNetAddConnection2(ref nr, password, username, 0);
            if (result != 0 && result != 1219) // 1219 = ERROR_SESSION_CREDENTIAL_CONFLICT
            {
                errorMessage = $"WNetAddConnection2 failed with error code: {result}";
                return false;
            }

            return true;
        }

        public static void DisconnectShare(string remoteUncPath)
        {
            try
            {
                WNetCancelConnection2(remoteUncPath, 0, true);
            }
            catch { }
        }
    }
}
