# Codex Usage Tray

[![Build](https://github.com/Yanghub-code/codex-usage-tray/actions/workflows/build.yml/badge.svg)](https://github.com/Yanghub-code/codex-usage-tray/actions/workflows/build.yml)
[![Release](https://img.shields.io/github/v/release/Yanghub-code/codex-usage-tray)](https://github.com/Yanghub-code/codex-usage-tray/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

A lightweight, open-source Windows taskbar meter for Codex quota and physical memory usage.

[简体中文](README.md)

> This is an unofficial community project and is not affiliated with or endorsed by OpenAI.

## Preview

![Codex Usage Tray preview](docs/images/taskbar-meter.png)

- **Purple**: remaining Codex quota.
- **Blue**: used physical memory.
- Percentages are always visible; hover for details.

## Features

- A compact, non-activating panel positioned immediately to the left of the Windows system tray.
- Automatically hides during true fullscreen apps (including browser fullscreen videos and presentations) on the primary taskbar display and reappears afterward. Maximized windows are not treated as fullscreen.
- Toggle **Auto-hide in fullscreen** from the panel's right-click menu. The setting persists in `%LOCALAPPDATA%\CodexUsageTray\settings.json`; readings keep updating while hidden.
- Keeps Windows sign-in startup and starts a lightweight local watcher that restores the meter when the Codex desktop app is started or restarted, without launching duplicate meter instances.
- Codex quota refreshes every 5 minutes by default.
- Memory usage refreshes every 30 minutes by default.
- Both values are read immediately at startup.
- Right-click actions for refresh, Codex, Task Manager, and exit.
- Per-user startup installation without administrator rights.
- Local-only operation with no telemetry upload.

## Requirements

- Windows 10 or Windows 11.
- Codex desktop or Codex CLI installed and signed in.
- .NET Framework 4.8.

## Install

1. Download `CodexUsageTray-v2.0.4-win-x64.zip` from the [latest release](https://github.com/Yanghub-code/codex-usage-tray/releases/latest).
2. Extract it.
3. Run:

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .\install.ps1
   ```

The installer copies the app to `%LOCALAPPDATA%\CodexUsageTray`, creates a per-user startup shortcut, and launches the app.

The release binary is currently unsigned. Verify the published SHA-256 checksum or build from source if Windows displays an origin warning.

## Uninstall

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\uninstall.ps1
```

## Build from source

```powershell
git clone https://github.com/Yanghub-code/codex-usage-tray.git
cd codex-usage-tray
.\scripts\build.ps1
```

The executable is written to `artifacts\bin\CodexUsageTray.exe`. No NuGet packages are required.

## Privacy and implementation

- Memory data is read locally through Windows `GlobalMemoryStatusEx`.
- Quota data is read from the local `codex app-server --stdio` interface.
- The app does not read Codex conversation content and does not upload telemetry.
- The repository contains no API keys, tokens, or user-specific paths.

The local Codex interface may change between product versions. Open an issue with the app version and a redacted error if quota reading stops working.

## License

[MIT License](LICENSE)

