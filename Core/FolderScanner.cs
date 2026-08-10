using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ProfileShift.Core
{
    public static class FolderScanner
    {
        public static readonly string[] SystemRootExclusions = new[]
        {
            "Windows", "Program Files", "Program Files (x86)", "ProgramData",
            "PerfLogs", "$Recycle.Bin", "System Volume Information", "Users",
            "pagefile.sys", "hiberfil.sys", "swapfile.sys"
        };

        public static readonly string[] StandardUserFolders = new[]
        {
            "Desktop", "Documents", "Downloads", "Music", "Pictures", "Videos",
            "Favorites", "Contacts", "Links", "Searches", "Saved Games"
        };

        public static List<string> GetRootDriveFolders()
        {
            var result = new List<string>();
            string rootPath = @"C:\";
            if (!Directory.Exists(rootPath)) return result;

            try
            {
                var dirs = Directory.GetDirectories(rootPath);
                foreach (var dir in dirs)
                {
                    string name = Path.GetFileName(dir);
                    if (!SystemRootExclusions.Contains(name, StringComparer.OrdinalIgnoreCase))
                    {
                        result.Add(dir);
                    }
                }
            }
            catch { }

            return result;
        }

        public static List<string> GetUserSelectableFolders(string userProfilePath)
        {
            var folders = new List<string>();
            if (!Directory.Exists(userProfilePath)) return folders;

            foreach (var folderName in StandardUserFolders)
            {
                string fullPath = Path.Combine(userProfilePath, folderName);
                if (Directory.Exists(fullPath))
                {
                    folders.Add(fullPath);
                }
            }

            string taskbarPins = Path.Combine(userProfilePath, @"AppData\Roaming\Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
            if (Directory.Exists(taskbarPins)) folders.Add(taskbarPins);

            string autoDest = Path.Combine(userProfilePath, @"AppData\Roaming\Microsoft\Windows\Recent\AutomaticDestinations");
            if (Directory.Exists(autoDest)) folders.Add(autoDest);

            string signatures = Path.Combine(userProfilePath, @"AppData\Roaming\Microsoft\Signatures");
            if (Directory.Exists(signatures)) folders.Add(signatures);

            return folders;
        }

        public static long CalculateTotalSize(List<string> folderPaths)
        {
            long totalBytes = 0;
            foreach (var path in folderPaths)
            {
                if (File.Exists(path))
                {
                    totalBytes += new FileInfo(path).Length;
                }
                else if (Directory.Exists(path))
                {
                    totalBytes += GetDirectorySize(path);
                }
            }
            return totalBytes;
        }

        private static long GetDirectorySize(string directoryPath)
        {
            long size = 0;
            try
            {
                var files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    if (file.Contains(@"\OneDrive\") || file.Contains(@"\SharePoint\") || file.Contains(@"\Dropbox\"))
                        continue;

                    try
                    {
                        size += new FileInfo(file).Length;
                    }
                    catch { }
                }
            }
            catch { }
            return size;
        }
    }
}
