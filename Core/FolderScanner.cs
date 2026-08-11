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
            string rootPath = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
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
                    totalBytes += GetDirectoryStats(path, System.Threading.CancellationToken.None).Bytes;
                }
            }
            return totalBytes;
        }

        public static System.Threading.Tasks.Task<(long TotalBytes, long FileCount)> CalculateTotalStatsAsync(List<string> folderPaths, System.Threading.CancellationToken cancellationToken)
        {
            return System.Threading.Tasks.Task.Run(() =>
            {
                long totalBytes = 0;
                long fileCount = 0;

                foreach (var path in folderPaths)
                {
                    if (cancellationToken.IsCancellationRequested) break;

                    if (File.Exists(path))
                    {
                        if (!ExclusionFilter.ShouldExcludeFile(path))
                        {
                            totalBytes += new FileInfo(path).Length;
                            fileCount++;
                        }
                    }
                    else if (Directory.Exists(path))
                    {
                        var (bytes, count) = GetDirectoryStats(path, cancellationToken);
                        totalBytes += bytes;
                        fileCount += count;
                    }
                }

                return (totalBytes, fileCount);
            }, cancellationToken);
        }

        private static (long Bytes, long Count) GetDirectoryStats(string directoryPath, System.Threading.CancellationToken cancellationToken)
        {
            long size = 0;
            long count = 0;
            var dirQueue = new Queue<string>();
            dirQueue.Enqueue(directoryPath);

            while (dirQueue.Count > 0)
            {
                if (cancellationToken.IsCancellationRequested) break;

                string currentDir = dirQueue.Dequeue();

                try
                {
                    foreach (var subDir in Directory.GetDirectories(currentDir))
                    {
                        string subName = Path.GetFileName(subDir);
                        if (!subName.Equals("OneDrive", StringComparison.OrdinalIgnoreCase) &&
                            !subName.Equals("SharePoint", StringComparison.OrdinalIgnoreCase) &&
                            !subName.Equals("Dropbox", StringComparison.OrdinalIgnoreCase) &&
                            !ExclusionFilter.ShouldExcludeDirectory(subDir))
                        {
                            dirQueue.Enqueue(subDir);
                        }
                    }
                }
                catch { }

                try
                {
                    foreach (var file in Directory.GetFiles(currentDir))
                    {
                        if (cancellationToken.IsCancellationRequested) break;

                        if (!ExclusionFilter.ShouldExcludeFile(file))
                        {
                            try
                            {
                                size += new FileInfo(file).Length;
                                count++;
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }

            return (size, count);
        }
    }
}
