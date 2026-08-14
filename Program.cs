using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ProfileShift.Core;
using ProfileShift.Models;
using ProfileShift.UI;

namespace ProfileShift
{
    public class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            if (args != null && args.Length > 0)
            {
                var argList = new List<string>(args);

                if (argList.Contains("--stage-restore"))
                {
                    var app = new Application();
                    app.Run(new StagingOverlayWindow());
                    return;
                }

                if (argList.Contains("--elevated-helper"))
                {
                    string backupDir = GetArgValue(argList, "--backup-dir");
                    if (string.IsNullOrEmpty(backupDir))
                    {
                        Console.Error.WriteLine("[ELEVATED] Error: --backup-dir required.");
                        Environment.Exit(1);
                        return;
                    }
                    int exitCode = ElevatedHelper.RunElevatedHelperCli(backupDir);
                    Environment.Exit(exitCode);
                    return;
                }

                if (argList.Contains("--elevated-restore"))
                {
                    string srcPath = GetArgValue(argList, "--src");
                    string usersArg = GetArgValue(argList, "--users");

                    if (string.IsNullOrEmpty(srcPath))
                    {
                        Console.Error.WriteLine("[ELEVATED] Error: --src required.");
                        Environment.Exit(1);
                        return;
                    }

                    var users = new List<string>(usersArg.Split('|', StringSplitOptions.RemoveEmptyEntries));
                    var restoreEngine = new RestoreEngine();
                    restoreEngine.LogMessage += (s, msg) => Console.WriteLine($"[ELEVATED] {msg}");

                    var task = restoreEngine.StageRestoreAsync(srcPath, users);
                    task.Wait();
                    Environment.Exit(task.Result ? 0 : 1);
                    return;
                }

                if (argList.Contains("--silent") || argList.Contains("--backup") || argList.Contains("--restore"))
                {
                    RunCliAsync(argList).Wait();
                    return;
                }
            }

            var guiApp = new Application();
            try
            {
                guiApp.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/UI/Themes/DarkTheme.xaml", UriKind.RelativeOrAbsolute)
                });
            }
            catch { }
            guiApp.Run(new MainWindow());
        }

        private static async Task RunCliAsync(List<string> args)
        {
            Console.WriteLine("=== ProfileShift CLI Engine ===");

            string configPath = GetArgValue(args, "--config");
            string destPath = GetArgValue(args, "--dest");
            string srcPath = GetArgValue(args, "--src");

            if (args.Contains("--backup"))
            {
                if (string.IsNullOrEmpty(destPath))
                {
                    Console.WriteLine("Error: Destination path (--dest) required for backup mode.");
                    return;
                }

                MigrationConfig? config = null;
                if (!string.IsNullOrEmpty(configPath) && File.Exists(configPath))
                {
                    config = ConfigManager.LoadAutoConfig(configPath);
                }

                var engine = new BackupEngine();
                engine.LogMessage += (s, msg) => Console.WriteLine($"[LOG] {msg}");

                var users = UserDetection.GetLocalUserProfiles();
                var rootFolders = FolderScanner.GetRootDriveFolders();
                var userFoldersMap = new Dictionary<string, List<string>>();
                var userBrowsersMap = new Dictionary<string, List<string>>();

                foreach (var u in users)
                {
                    userFoldersMap[u.Username] = FolderScanner.GetUserSelectableFolders(u.ProfilePath);
                    var available = BrowserDetector.GetAvailableBrowsers(u.ProfilePath);
                    userBrowsersMap[u.Username] = available.FindAll(b => b.IsInstalled).ConvertAll(b => b.RelativePath);
                }

                bool exportCreds = args.Contains("--export-credentials");
                bool exportBrowserPw = args.Contains("--export-browser-passwords");
                string passphrase = GetArgValue(args, "--passphrase");

                await engine.RunBackupAsync(destPath, users, rootFolders, userFoldersMap, userBrowsersMap, CancellationToken.None,
                    exportCredentialManager: exportCreds,
                    exportBrowserPasswords: exportBrowserPw,
                    browserPasswordMode: "native",
                    credentialPassphrase: passphrase);
            }
            else if (args.Contains("--restore"))
            {
                if (string.IsNullOrEmpty(srcPath))
                {
                    Console.WriteLine("Error: Source path (--src) required for restore mode.");
                    return;
                }

                var restoreEngine = new RestoreEngine();
                restoreEngine.LogMessage += (s, msg) => Console.WriteLine($"[LOG] {msg}");

                string cfg = Path.Combine(srcPath, "Migration.json");
                if (!File.Exists(cfg)) cfg = Path.Combine(srcPath, "Migration.yaml");

                MigrationConfig? loaded = ConfigManager.LoadAutoConfig(cfg);
                if (loaded != null)
                {
                    string passphrase = GetArgValue(args, "--passphrase");
                    if (!string.IsNullOrEmpty(passphrase))
                    {
                        string credManagerFile = Path.Combine(srcPath, "CredentialManager.dat");
                        string browserPwFile = Path.Combine(srcPath, "BrowserPasswords.dat");
                        if (File.Exists(credManagerFile)) CredentialManagerExporter.ImportCredentials(srcPath, passphrase, msg => Console.WriteLine($"[LOG] {msg}"));
                        if (File.Exists(browserPwFile)) BrowserPasswordExporter.ImportBrowserPasswords(srcPath, passphrase, msg => Console.WriteLine($"[LOG] {msg}"));
                    }

                    await restoreEngine.StageRestoreAsync(srcPath, loaded.SelectedUsers);
                }
            }
        }

        private static string GetArgValue(List<string> args, string key)
        {
            int idx = args.IndexOf(key);
            if (idx >= 0 && idx + 1 < args.Count)
            {
                return args[idx + 1];
            }
            return string.Empty;
        }
    }
}
