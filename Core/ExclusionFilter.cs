using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ProfileShift.Core
{
    public static class ExclusionFilter
    {
        public static readonly string[] DefaultExcludedExtensions = new[]
        {
            ".tmp", ".bak", ".iso", ".vhd", ".vhdx", ".sys", ".dmp"
        };

        public static readonly string[] DefaultExcludedDirectories = new[]
        {
            "node_modules", ".git", "Cache", "GPUCache", "Crashpad", "Code Cache", "temp", "tmp",
            "ProfileShift_Backup*", "System_ProfileShift_Staging*", "$Windows.~BT", "$Windows.~WS", "$WinREAgent", "Recovery"
        };

        public static bool ShouldExcludeFile(string filePath, IEnumerable<string>? customExtensions = null)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return true;

            string fileName = Path.GetFileName(filePath);
            if (string.Equals(fileName, "desktop.ini", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(fileName, "Thumbs.db", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            var extensions = customExtensions ?? DefaultExcludedExtensions;
            if (extensions.Contains(ext, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        public static bool ShouldExcludeDirectory(string dirPath, IEnumerable<string>? customDirs = null)
        {
            if (string.IsNullOrWhiteSpace(dirPath)) return true;

            string dirName = Path.GetFileName(dirPath);

            foreach (var pattern in DefaultExcludedDirectories)
            {
                if (MatchesPattern(dirName, pattern))
                {
                    return true;
                }
            }

            if (customDirs != null)
            {
                foreach (var custom in customDirs)
                {
                    if (string.IsNullOrWhiteSpace(custom)) continue;

                    if (custom.Contains(Path.DirectorySeparatorChar) || custom.Contains(Path.AltDirectorySeparatorChar))
                    {
                        if (IsSameOrSubdirectory(custom, dirPath))
                        {
                            return true;
                        }
                    }
                    else
                    {
                        if (MatchesPattern(dirName, custom))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        public static bool IsSameOrSubdirectory(string basePath, string candidatePath)
        {
            if (string.IsNullOrWhiteSpace(basePath) || string.IsNullOrWhiteSpace(candidatePath)) return false;
            try
            {
                string normalizedBase = Path.GetFullPath(basePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string normalizedCandidate = Path.GetFullPath(candidatePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                if (string.Equals(normalizedBase, normalizedCandidate, StringComparison.OrdinalIgnoreCase))
                    return true;

                return normalizedCandidate.StartsWith(normalizedBase + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static bool MatchesPattern(string name, string pattern)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(pattern)) return false;

            if (pattern.EndsWith("*", StringComparison.Ordinal))
            {
                string prefix = pattern.Substring(0, pattern.Length - 1);
                return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
            }
            if (pattern.StartsWith("*", StringComparison.Ordinal))
            {
                string suffix = pattern.Substring(1);
                return name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
            }
            return string.Equals(name, pattern, StringComparison.OrdinalIgnoreCase);
        }
    }
}

