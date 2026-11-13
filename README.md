# monitor-Fader-App

A lightweight standalone Windows utility that dims inactive monitors (inspired by DisplayFusion's Monitor Fading feature), built from scratch in C# using native Windows APIs with zero external dependencies.

## Features
- **Enable / Disable Monitor Fading** from the UI, system tray, or global hotkey
- **Fading Modes:**
  - Fade Non-Current Monitors (Active Window)
  - Fade Non-Current Monitors (Mouse Cursor)
  - Fade All Monitors
- **Exclude Fullscreen Windows** (automatically detects true fullscreen apps/games without falsely excluding maximized windows)
- **Global Hotkey Support** (defaults to `Num Enter`; supports `Ctrl`/`Alt`/`Shift`/`Win` + key combinations)
- **Custom Fade Color, Fade Time (ms), and Opacity (1%–100%)** with smooth ~60 FPS transitions
- **Excluded Monitors Visual Map** — click any monitor to exclude/include it from fading
- **Per-Monitor DPI Aware** and click-through overlays (`WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE`)
- **System Tray & Start with Windows** (`--minimized`)

## Build
Run the PowerShell build script (uses the built-in .NET Framework `csc.exe`, no SDK install required):

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

This outputs `MonitorFader.exe`.
