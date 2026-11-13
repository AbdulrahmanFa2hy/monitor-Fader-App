# Monitor Fader

A lightweight standalone Windows multi-monitor utility (inspired by DisplayFusion's Monitor Fading, TitleBar Buttons, and Multi-Monitor Taskbar features), built from scratch in C# using native Windows APIs with zero external dependencies.

## Download

- **[Download Latest Release (`MonitorFader.exe` / `MonitorFader-v1.0.0.zip`)](https://github.com/AbdulrahmanFa2hy/monitor-Fader-App/releases/latest)**
- Single portable `.exe` (~128 KB) — no installation or extra runtime required on Windows 10/11.

## Features

### Monitor Fading
- **Enable / Disable Monitor Fading** from the UI, system tray, or global hotkey
- **Fading Modes:**
  - Fade Non-Current Monitors (Active Window)
  - Fade Non-Current Monitors (Mouse Cursor)
  - Fade All Monitors
- **Exclude Fullscreen Windows** (automatically detects true fullscreen apps/games without falsely excluding maximized windows)
- **Global Hotkey Support** (defaults to `Num Enter`; supports `Ctrl`/`Alt`/`Shift`/`Win` + key combinations)
- **Custom Fade Color, Fade Time (ms), and Opacity (1%–100%)** with smooth ~60 FPS transitions
- **Excluded Monitors Visual Map** — click any monitor to exclude/include it from fading

### TitleBar Buttons
- Adds a **Move Window to Next Monitor** button directly next to the Minimize button on open windows (`WM_GETTITLEBARINFOEX` + DWM fallback)
- Left-click moves to the next monitor; Middle-click / Right-click / Shift+Left-click moves to the previous monitor
- Preserves maximized state and scales proportional window size/position across mixed-DPI monitors

### Multi-Monitor Taskbars
- Adds a native-style taskbar (`SHAppBarMessage`) to each secondary monitor with Start button, live window buttons, and clock
- **Taskbar Mode:** Show relevant windows per monitor or all windows on all taskbars
- **Middle-Click / Shift+Click Behavior:** Open a new instance, Close window, or Do nothing
- **Window Preview Style:** Live DWM thumbnails (`DwmRegisterThumbnail`), text tooltips, or Off
- **Fullscreen Handling & Drag-Hover Focus:** Show taskbar in front of fullscreen apps when they lose focus, and activate windows when dragging over taskbar buttons
- **Taskbar Opacity** slider (`1%`–`100%`)

### System Integration
- **Start with Windows** (auto-enabled via `HKCU\...\Run` with `--minimized`)
- **System Tray** quick toggles & Per-Monitor DPI awareness

## Build
Run the PowerShell build script (uses the built-in .NET Framework `csc.exe`, no SDK install required):

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

This outputs `MonitorFader.exe`.
