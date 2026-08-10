# Hat's ProfileShift 🚀

**ProfileShift** is a high-performance, open-source Windows user profile migration utility compiled as a portable single-file `.exe`. Designed for IT administrators, MSP technicians, and power users, ProfileShift facilitates backing up, transferring, and restoring Windows user profiles, web browser data, wallpaper, system theme personalizations, taskbar pins, start menu layouts, network drive mappings, printers, default application associations, and environment variables.

---

## Key Features

- **Standalone Single-File Executable**: Compiles into a single `ProfileShift.exe` binary with zero external runtime or framework installation requirements.
- **Dual Interface Modes**:
  - **Modern Dark WPF GUI**: Built with a sleek dark theme (`#2F3136`) and native Windows DWM dark title bars.
  - **Silent Headless CLI**: Command-line orchestration (`--backup`, `--restore`, `--config`, `--silent`) supporting **JSON** and **YAML** configuration files.
- **Web Browser Data Selector**: Auto-detects Chrome, Edge, Firefox, Opera, Brave, and Vivaldi profiles per user.
- **Granular Folder & Subdirectory Tree View**: Interactive tree-view modal allowing selective inclusion/exclusion of specific subfolders.
- **Start Menu & Taskbar Pin Migration**: Preserves Windows 10 `LayoutModification.xml`, Windows 11 `start.bin`, and Taskbar shortcuts.
- **Default Application Associations (DISM)**: Exports and imports file handler defaults for web browsers, PDFs, and media players.
- **Pre-Flight Disk Space Checks**: Queries available target drive space before executing file transfers.
- **ForensIT-Style Logon Staging**: Staging engine (`C:\System_ProfileShift_Staging`) with a full-screen, top-most logon overlay window that locks input and displays real-time profile restoration progress.
- **User Environment Variables (`HKCU\Environment`)**: Restores user environment variables and broadcasts `WM_SETTINGCHANGE` for instant applicability without requiring a system reboot.
- **Desktop HTML Summary Reports**: Generates a clean, standalone HTML summary report (`ProfileShift_Summary.html`) placed directly on the user's Desktop.
- **GitHub Auto-Update Notifier**: Checks GitHub API on startup to notify users of new releases.

---

## Project Architecture

```
ProfileShift/
├── ProfileShift.csproj              # .NET 8 WPF Single-File Executable Configuration
├── Program.cs                       # Entry Point (CLI Argument Router vs WPF GUI Startup)
├── Core/
│   ├── UserDetection.cs             # Local and domain user profile hive enumerator
│   ├── BrowserDetector.cs           # Chrome, Edge, Firefox, Opera, Brave, Vivaldi profile scanner
│   ├── FolderScanner.cs             # Folder indexer with system and cloud exclusion filters
│   ├── SettingsMigrator.cs          # Registry, theme, wallpaper, printers & DISM app associations
│   ├── PreFlightChecker.cs          # Disk space validation & warning service
│   ├── CompressionManager.cs        # Zip archive creation & extraction
│   ├── ReportGenerator.cs           # HTML desktop summary report generator
│   ├── StartMenuMigrator.cs         # Windows 10/11 Start Menu and Taskbar layout migration
│   ├── ScriptHookEngine.cs          # Pre-backup & post-restore script runner (.ps1 / .bat)
│   ├── EnvironmentMigrator.cs       # HKCU\Environment export/import & setting change broadcast
│   ├── UpdateChecker.cs             # GitHub Releases API version checker
│   ├── BackupEngine.cs              # Multithreaded file transfer & integrity validation engine
│   ├── RestoreEngine.cs             # Profile staging manager & scheduled logon task installer
│   └── ConfigManager.cs             # JSON & YAML configuration serializers
├── UI/
│   ├── MainWindow.xaml / .cs        # Primary dark-theme WPF application window
│   ├── StagingOverlayWindow.xaml    # Full-screen logon blocker & progress window
│   └── Views/
│       └── FolderPickerModal.xaml   # Hierarchical tree-view subfolder selection modal
└── Utilities/
    └── DwmHelper.cs                 # Native DWM dark title bar API integration
```

---

## Quick Start & Usage

### 1. Graphical User Interface (GUI)
Launch `ProfileShift.exe` on any Windows 10 or 11 system:
1. **Backup Tab**: Select the destination directory, check the target user accounts, adjust folder options (or launch the **Customize Subfolders** tree view picker), and click **Start Backup**.
2. **Restore Tab**: Select a previously generated `ProfileShift_Backup_YYYYMMDD_HHMMSS` directory and click **Stage Migration Data**.

### 2. Silent Command-Line Interface (CLI)

#### Automated Backup (JSON or YAML)
```cmd
ProfileShift.exe --backup --dest "E:\Backups" --config "C:\path\to\config.yaml" --silent
```

#### Profile Restore Staging
```cmd
ProfileShift.exe --restore --src "E:\Backups\ProfileShift_Backup_20260810_174500" --silent
```

---

## Configuration Schema Example (YAML)

```yaml
robocopyThreads: 16
preBackupHookScript: "C:\\Scripts\\PreBackup.ps1"
postRestoreHookScript: "C:\\Scripts\\PostRestore.ps1"
selectedUsers:
  - tylerhats
selectedRootFolders:
  - "C:\\Tools"
userSelections:
  tylerhats:
    folders:
      - "C:\\Users\\tylerhats\\Desktop"
      - "C:\\Users\\tylerhats\\Documents"
    browsers:
      - "AppData\\Local\\Google\\Chrome\\User Data"
      - "AppData\\Local\\Microsoft\\Edge\\User Data"
```

---

## Building from Source

To build `ProfileShift.exe` from source, ensure [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) is installed:

```bash
# Clone the repository
git clone https://github.com/TylerHats/UserMoveTool.git
cd UserMoveTool

# Restore dependencies
dotnet restore ProfileShift.csproj

# Build Debug
dotnet build ProfileShift.csproj

# Publish Portable Single-File Binary (Windows x64)
dotnet publish ProfileShift.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

The published portable executable will be generated at:
`bin/Release/net8.0-windows/win-x64/publish/ProfileShift.exe`

---

## License

This project is licensed under the [MIT License](LICENSE).
