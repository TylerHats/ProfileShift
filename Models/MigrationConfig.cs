using System;
using System.Collections.Generic;

namespace ProfileShift.Models
{
    public class MigrationConfig
    {
        public string OSBuild { get; set; } = string.Empty;
        public string SourceMachineName { get; set; } = string.Empty;
        public string SourceDomain { get; set; } = string.Empty;
        public DateTime MigrationTime { get; set; } = DateTime.Now;

        public int RobocopyThreads { get; set; } = 16;
        public string PreBackupHookScript { get; set; } = string.Empty;
        public string PostRestoreHookScript { get; set; } = string.Empty;

        public List<string> SelectedUsers { get; set; } = new List<string>();
        public List<string> SelectedRootFolders { get; set; } = new List<string>();
        public Dictionary<string, UserSelection> UserSelections { get; set; } = new Dictionary<string, UserSelection>();

        public List<PrinterInfo> Printers { get; set; } = new List<PrinterInfo>();
        public List<string> SystemSoftware { get; set; } = new List<string>();
        public Dictionary<string, List<string>> UserSoftware { get; set; } = new Dictionary<string, List<string>>();
    }

    public class UserSelection
    {
        public List<string> Folders { get; set; } = new List<string>();
        public List<string> Browsers { get; set; } = new List<string>();
        public UserSettings Settings { get; set; } = new UserSettings();
        public Dictionary<string, string> EnvironmentVariables { get; set; } = new Dictionary<string, string>();
        public bool CredentialManagerExported { get; set; }
        public bool BrowserPasswordsExported { get; set; }
        public string BrowserPasswordExportMode { get; set; } = string.Empty;
    }

    public class UserSettings
    {
        public int? AppsUseLightTheme { get; set; }
        public int? SystemUsesLightTheme { get; set; }
        public int? ColorPrevalence { get; set; }
        public int? TaskbarAl { get; set; }
        public int? ShowTaskViewButton { get; set; }
        public int? TaskbarDa { get; set; }
        public int? SearchboxTaskbarMode { get; set; }
        public string WallpaperPath { get; set; } = string.Empty;
        public List<DriveMapInfo> MappedDrives { get; set; } = new List<DriveMapInfo>();
    }

    public class DriveMapInfo
    {
        public string DriveLetter { get; set; } = string.Empty;
        public string RemotePath { get; set; } = string.Empty;
    }

    public class PrinterInfo
    {
        public string Name { get; set; } = string.Empty;
        public string DriverName { get; set; } = string.Empty;
        public string PortName { get; set; } = string.Empty;
        public bool Shared { get; set; }
    }

    public class BrowserOption
    {
        public string Name { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public bool IsInstalled { get; set; }
    }

    public class ExportedCredential
    {
        public string TargetName { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public int CredentialType { get; set; }
        public int Persistence { get; set; }
    }

    public class BrowserPasswordEntry
    {
        public string Browser { get; set; } = string.Empty;
        public string ProfileName { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}
