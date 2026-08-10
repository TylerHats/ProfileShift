using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace UserMoveTool.Core
{
    public class RestoreEngine
    {
        public event EventHandler<string>? LogMessage;
        public event EventHandler<int>? ProgressChanged;

        public async Task<bool> StageRestoreAsync(string backupSourcePath, List<string> selectedUsers)
        {
            string publicStaging = @"C:\System_Profile_Migration";

            try
            {
                OnLog("Creating secure staging folder: " + publicStaging);
                Directory.CreateDirectory(publicStaging);

                // Set ACLs to SYSTEM and Administrators
                RunCmd($"icacls \"{publicStaging}\" /inheritance:r /grant \"SYSTEM:(OI)(CI)F\" \"Administrators:(OI)(CI)F\" /T /C /Q");

                // Copy Config files
                string jsonConfig = Path.Combine(backupSourcePath, "Migration.json");
                string yamlConfig = Path.Combine(backupSourcePath, "Migration.yaml");

                if (File.Exists(jsonConfig)) File.Copy(jsonConfig, Path.Combine(publicStaging, "Migration.json"), true);
                if (File.Exists(yamlConfig)) File.Copy(yamlConfig, Path.Combine(publicStaging, "Migration.yaml"), true);

                string filesStaging = Path.Combine(publicStaging, "StagedFiles");
                Directory.CreateDirectory(filesStaging);

                string dataRoot = Path.Combine(backupSourcePath, "C_Drive");

                int idx = 0;
                foreach (string username in selectedUsers)
                {
                    idx++;
                    int pct = (int)((double)idx / selectedUsers.Count * 90);
                    OnProgress(pct);
                    OnLog($"Staging files for user: {username}");

                    string srcUser = Path.Combine(dataRoot, "Users", username);
                    string dstUser = Path.Combine(filesStaging, username);

                    if (Directory.Exists(srcUser))
                    {
                        await Task.Run(() => RunCmd($"robocopy \"{srcUser}\" \"{dstUser}\" /E /COPY:DAT /DCOPY:DAT /R:0 /W:0 /MT:16 /NFL /NDL /NJH /NJS /nc /ns /np"));
                    }
                }

                // Copy Non-Users root directories
                if (Directory.Exists(dataRoot))
                {
                    foreach (var rootDir in Directory.GetDirectories(dataRoot))
                    {
                        string dirName = Path.GetFileName(rootDir);
                        if (!string.Equals(dirName, "Users", StringComparison.OrdinalIgnoreCase))
                        {
                            string targetDriveDir = Path.Combine(@"C:\", dirName);
                            await Task.Run(() => RunCmd($"robocopy \"{rootDir}\" \"{targetDriveDir}\" /E /COPY:DAT /DCOPY:DAT /R:0 /W:0 /MT:16 /NFL /NDL /NJH /NJS /nc /ns /np"));
                        }
                    }
                }

                // Register Scheduled Task to trigger staging overlay on logon
                string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UserMoveTool.exe");
                string taskXml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <Triggers><LogonTrigger><Enabled>true</Enabled></LogonTrigger></Triggers>
  <Principals><Principal id=""Author""><UserId>S-1-5-18</UserId><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
  <Settings><MultipleInstancesPolicy>Parallel</MultipleInstancesPolicy><ExecutionTimeLimit>PT0S</ExecutionTimeLimit></Settings>
  <Actions Context=""Author""><Exec><Command>{exePath}</Command><Arguments>--stage-restore</Arguments></Exec></Actions>
</Task>";

                string xmlPath = Path.Combine(Path.GetTempPath(), "MigrationTask.xml");
                File.WriteAllText(xmlPath, taskXml, Encoding.Unicode);

                RunCmd($"schtasks.exe /create /tn \"WindowsSetupIntegration\" /xml \"{xmlPath}\" /f");

                OnLog("Profile staging complete! Please log out and log back in to trigger profile setup.");
                OnProgress(100);
                return true;
            }
            catch (Exception ex)
            {
                OnLog($"Error during staging: {ex.Message}");
                return false;
            }
        }

        private void RunCmd(string command)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {command}",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var p = Process.Start(psi);
            p?.WaitForExit();
        }

        private void OnLog(string msg) => LogMessage?.Invoke(this, msg);
        private void OnProgress(int pct) => ProgressChanged?.Invoke(this, pct);
    }
}
