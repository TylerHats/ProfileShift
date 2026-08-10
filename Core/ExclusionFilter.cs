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
            "node_modules", ".git", "Cache", "GPUCache", "Crashpad", "Code Cache", "temp", "tmp"
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
            var dirs = customDirs ?? DefaultExcludedDirectories;
            if (dirs.Contains(dirName, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }
    }
}
