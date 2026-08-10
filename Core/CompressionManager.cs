using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace ProfileShift.Core
{
    public static class CompressionManager
    {
        public static Task CompressDirectoryAsync(string sourceDir, string destinationZipFile, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                if (File.Exists(destinationZipFile))
                {
                    File.Delete(destinationZipFile);
                }

                ZipFile.CreateFromDirectory(sourceDir, destinationZipFile, CompressionLevel.Optimal, false);
            }, cancellationToken);
        }

        public static Task ExtractZipArchiveAsync(string zipFilePath, string extractDir, CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                if (!Directory.Exists(extractDir))
                {
                    Directory.CreateDirectory(extractDir);
                }

                ZipFile.ExtractToDirectory(zipFilePath, extractDir, true);
            }, cancellationToken);
        }
    }
}
