# Changelog

All notable changes to this project are documented here.

## [2.0.4] - 2026-10-22

### Added

- Start a lightweight local watcher when the meter starts; it restores the meter after Codex desktop is started or restarted.
- The watcher recognizes the installed Codex desktop package only, prevents duplicate meter instances, and keeps a manual tray-menu exit respected until Codex is opened again.

## [2.0.3] - 2026-10-22

### Added

- Automatically hide the taskbar-adjacent meter during true fullscreen apps; restore after exiting fullscreen.
- Right-click toggle for fullscreen auto-hide, enabled by default and persisted per user.
- Fullscreen detection scoped to the primary taskbar display; normal maximized windows stay visible.

## [2.0.2] - 2026-10-16

### Added

- Purple battery meter for remaining Codex quota.
- Blue battery meter for used physical memory.
- Always-visible percentages in a taskbar-adjacent panel.
- Hover details and a right-click action menu.
- Per-user startup installer and uninstaller.
- Local `status.json` output for integrations.

### Changed

- Codex quota refresh interval is now 5 minutes.
- Memory refresh interval is now 30 minutes.
- Both readings still refresh immediately on startup.

## [1.1.0] - 2026-10-15

- Initial two-icon notification-area implementation.

