# xTaskForge Script and Tray Configuration Guide

xTaskForge runs on Windows and supports ordinary PowerShell, batch/CMD, and executable tasks.

## Where configuration lives

- `%LOCALAPPDATA%\xTaskForge\scripts\` — put your scripts and helper executables here.
  - `CycleAudioOutput.ps1` — script to trigger audio output device cycling via local IPC.
  - `Example.ps1` — sample automation script.
- `%LOCALAPPDATA%\xTaskForge\settings.json` — tasks, launcher programs, arguments, and startup settings.
- `%LOCALAPPDATA%\xTaskForge\tray_menu.json` — tray menu entries, double-click action, and default tray icon.

You can configure items directly through the UI:
- **Automation Tasks tab:** click **+ Add Task** to add tasks, **✏ Edit Task** to adjust launchers/arguments, **📝 Edit Script File** to open any script in Notepad, or double-click any task.
- **Tray & Scripting IPC tab:** click **+ Add**, **✏ Edit**, **🗑 Remove**, or **▲ / ▼** to manage tray menu items visually, or double-click any item in the table. The table columns automatically stretch to fill the full width.

## Local Windows Named Pipe IPC (`\\.\pipe\xTaskForgePipe`)

xTaskForge uses a 100% local Windows Named Pipe for inter-process communication:
```powershell
# Send command via Windows Named Pipe:
[System.IO.File]::WriteAllText('\\.\pipe\xTaskForgePipe', '{"command":"audio.cycle"}')
```

Supported namespaced commands:
- `audio.listDevices` — list active output devices.
- `audio.cycle` — switch to next audio output device.
- `audio.getVolume` / `audio.setVolume` — query or set master volume level.
- `audio.getMute` / `audio.setMute` — query or set mute state.
- `display.listModes` — list supported display modes.
- `display.applyProfile` / `display.setResolution` — apply display resolution.
- `display.toggleHdr` — toggle HDR (`Win+Alt+B`).
- `power.preventSleep` / `power.releaseSleep` — manage sleep prevention.
- `task.run` — execute registered task by ID.
- `icon.set` / `tooltip.set` — set tray icon or tooltip text.
- `state.set` / `state.get` / `state.toggle` / `state.clear` — manage dynamic state flags.
- `condition.evaluate` — test any condition expression (`process:...`, `pip.active`, etc.).

## Dynamic Tray Menu Conditions ("Only show when" & "Alternate state when")

Tray menu items can dynamically show/hide (`visibleWhen`) or switch to alternate titles/commands (`toggledWhen`).

### Supported Condition Syntax

| Condition Format | Description | Example |
|---|---|---|
| `process:name.exe` | True if process is running | `process:notepad.exe` or `process:chrome` |
| `process:name.exe arguments` | True if process is running with specific command-line arguments | `process:chrome.exe --enable-features` or `process:powershell.exe MyScript.ps1` |
| `task:task_id` | True if an xTaskForge automation task is running | `task:audio_tray_monitor` |
| `state:key_name` | True if dynamic state variable is set to true / 1 | `state:game_mode` |
| `script:path/to/script.ps1` | True if script executes and exits with code 0 | `script:scripts/CheckOnline.ps1` |
| `window:Title Substring` | True if any visible desktop window title matches | `window:Visual Studio` |
| `audio.playing` | True if system audio is currently playing | `audio.playing` |
| `pip.active` | True if Picture-in-Picture window is active | `pip.active` |
| `pip.paused` | True if Picture-in-Picture click-through is paused | `pip.paused` |

### Condition Operators & Modifiers
- **Negation (`!`):** Invert condition result (e.g. `!process:game.exe` shows item when game is NOT running).
- **Logical AND (`&&`):** Combine conditions (e.g. `process:vlc.exe && !pip.active`).
- **Logical OR (`||`):** Combine conditions (e.g. `process:obs64.exe || state:streaming`).

### Dynamic State Variables via Script / IPC
You can set or toggle state variables from any PowerShell script, batch file, or IPC call to dynamically update tray items:

**PowerShell via Named Pipe:**
```powershell
# Set state variable:
[System.IO.File]::WriteAllText('\\.\pipe\xTaskForgePipe', '{"command":"state.set","key":"game_mode","value":"true"}')

# Toggle state variable:
[System.IO.File]::WriteAllText('\\.\pipe\xTaskForgePipe', '{"command":"state.toggle","key":"game_mode"}')

# Clear state variable:
[System.IO.File]::WriteAllText('\\.\pipe\xTaskForgePipe', '{"command":"state.clear","key":"game_mode"}')
```

**Native Action Commands:**
- Command `native:state_set:game_mode=true` — sets `state:game_mode` to true.
- Command `native:state_toggle:game_mode` — toggles `state:game_mode`.
- Command `native:state_clear:game_mode` — clears `state:game_mode`.

## Chrome Native Messaging Protocol (`com.xtaskforge.helper`)

xTaskForge includes built-in support for Chrome Extensions using Chrome Native Messaging:
1. Register `com.xtaskforge.helper` manifest in `HKCU\Software\Google\Chrome\NativeMessagingHosts\com.xtaskforge.helper`.
2. Chrome extension sends length-prefixed JSON packets to `xTaskForge.exe --native-messaging`.
3. Commands are checked against allowed registry capabilities and executed safely via local IPC.

## Supported task types

| `ScriptPath` extension | Default launcher |
|---|---|
| `.ps1` | `powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File` |
| `.bat`, `.cmd` | `cmd.exe /d /s /c` |
| `.exe` | Runs the executable directly |

For custom launchers, use `{script}` in `Arguments` where the quoted script path belongs.
