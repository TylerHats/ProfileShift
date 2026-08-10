using System;
using System.IO;

namespace ProfileShift.Core
{
    public class PreFlightCheckResult
    {
        public bool IsValid { get; set; }
        public long AvailableFreeSpace { get; set; }
        public long RequiredSpace { get; set; }
        public string WarningMessage { get; set; } = string.Empty;
        public string DriveRoot { get; set; } = string.Empty;
    }

    public static class PreFlightChecker
    {
        public static PreFlightCheckResult CheckDestinationSpace(string destinationPath, long estimatedRequiredBytes)
        {
            var result = new PreFlightCheckResult
            {
                RequiredSpace = estimatedRequiredBytes
            };

            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(destinationPath)) ?? destinationPath;
                result.DriveRoot = root;

                var driveInfo = new DriveInfo(root);
                result.AvailableFreeSpace = driveInfo.AvailableFreeSpace;

                if (result.AvailableFreeSpace < estimatedRequiredBytes)
                {
                    result.IsValid = false;
                    double requiredGb = Math.Round(estimatedRequiredBytes / 1073741824.0, 2);
                    double availableGb = Math.Round(result.AvailableFreeSpace / 1073741824.0, 2);
                    result.WarningMessage = $"Insufficient disk space on {root}. Required: {requiredGb} GB, Available: {availableGb} GB.";
                }
                else
                {
                    result.IsValid = true;
                    double availableGb = Math.Round(result.AvailableFreeSpace / 1073741824.0, 2);
                    result.WarningMessage = $"Space check passed on {root}. Available: {availableGb} GB.";
                }
            }
            catch (Exception ex)
            {
                result.IsValid = true;
                result.WarningMessage = $"Could not query drive space: {ex.Message}";
            }

            return result;
        }
    }
}
