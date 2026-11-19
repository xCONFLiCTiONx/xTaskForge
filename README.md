# xTaskForge

xTaskForge is a Windows 11/.NET 8 WPF tray utility for background audio-aware sleep prevention, display resolution/HDR shortcuts, configurable script launching, and dynamic tray menu customization. User-defined automation can be PowerShell, batch/CMD, or any executable.

## Built-in & Configurable Behavior

- **Windows Startup Integration:** toggle "Start with Windows" directly in the application window. It manages `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` automatically.
- **Native Audio Tray Icon Management:** event-driven WASAPI audio detection updates the tray icon directly in C# (`icon1.ico` when playing, `icon.ico` when silent) with zero polling overhead.
- **Audio-aware Sleep Prevention:** multi-reason reference-counted sleep prevention keeps Windows awake while audio is playing or tasks are active, restoring normal sleep behavior when idle.
- **Dynamic Tray Menu Customization:** add, edit, reorder (Move Up/Down), test run, and remove tray menu items directly from the UI, with full auto-stretching table columns.
- **Display Controls:** built-in native shortcuts & profiles for 3840×2160, 1920×1080, and HDR toggle (`Win+Alt+B`).
- **Configurable Automation:** add, edit, and launch `.ps1`, `.bat`, `.cmd`, or `.exe` tasks. Double-click any task to configure launcher executable, arguments, and working directory, or click "Edit Script File" to open in Notepad.
- **Secured Local Windows Named Pipe IPC:** control tray icons, menu items, audio capabilities, and tasks securely via `\\.\pipe\xTaskForgePipe` (protected by Windows User SID ACLs, no HTTP network listener).
- **Chrome Native Messaging Support:** supports Chrome Extension automation via `com.xtaskforge.helper` Native Messaging protocol (`xTaskForge.exe --native-messaging`).

## Configuration Locations

- Scripts: `%LOCALAPPDATA%\xTaskForge\scripts\`
- Task definitions and app preferences: `%LOCALAPPDATA%\xTaskForge\settings.json`
- Tray menu and double-click preferences: `%LOCALAPPDATA%\xTaskForge\tray_menu.json`

You can edit everything directly in the app UI, or click **Edit settings.json** / **Edit tray_menu.json** to modify the JSON files directly.

## Build

A GitHub Actions workflow builds the Windows WPF project on pushes and pull requests. You can also build from the repository root on Windows with the .NET 8 SDK:

```powershell
dotnet restore
dotnet build -c Release
dotnet run
```
