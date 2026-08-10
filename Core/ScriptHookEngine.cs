using System;
using System.Diagnostics;
using System.IO;

namespace ProfileShift.Core
{
    public static class ScriptHookEngine
    {
        public static bool RunScriptHook(string scriptPath, out string outputLog)
        {
            outputLog = string.Empty;
            if (string.IsNullOrWhiteSpace(scriptPath) || !File.Exists(scriptPath))
            {
                outputLog = "Script file not found: " + scriptPath;
                return false;
            }

            try
            {
                string ext = Path.GetExtension(scriptPath).ToLowerInvariant();
                ProcessStartInfo psi;

                if (ext == ".ps1")
                {
                    psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = $"-ExecutionPolicy Bypass -NoProfile -File \"{scriptPath}\"",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                }
                else
                {
                    psi = new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = $"/c \"{scriptPath}\"",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                }

                using var p = Process.Start(psi);
                if (p != null)
                {
                    string stdout = p.StandardOutput.ReadToEnd();
                    string stderr = p.StandardError.ReadToEnd();
                    p.WaitForExit(30000); // 30 sec timeout

                    outputLog = stdout + (string.IsNullOrWhiteSpace(stderr) ? "" : "\nSTDERR: " + stderr);
                    return p.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                outputLog = "Script execution error: " + ex.Message;
            }

            return false;
        }
    }
}
