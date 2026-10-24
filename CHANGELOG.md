# Changelog

All notable changes. Format follows [Keep a Changelog](https://keepachangelog.com); versions follow `Directory.Build.props`.

## [Unreleased]

### Added
- Tests: `tests/Verso.App.Tests` (storage, sanitising, launch and navigation rules) and `tests/web` (manifests, links,
  Content-Security-Policy). CI runs both on every push and pull request.
- Installer (Inno Setup, per-user, self-contained) and a tag-driven release workflow. See `docs/releasing.md`.
- Application log in `%APPDATA%\Verso\logs` (seven days kept); tray menu "Open logs folder".
- "Start with Windows" in the tray menu.
- `--restore-desktop` command line switch: puts the taskbar and desktop icons back and exits.
- `tools/probe-wallpaper`: a wallpaper page that reports whether it can reach the host bridge.
- `docs/security.md`.

### Changed
- Every WebView2 window is locked to Verso's own pages: no foreign navigation, new windows, downloads or permission
  prompts. The dashboard, desk, picker and editor pages carry a Content-Security-Policy; the picker and editor scripts
  moved into their own files for it.
- Interactive wallpapers load in a sandboxed frame.
- Only full paths and `shell:AppsFolder\...` addresses can be launched from a launcher tile.
- Settings, notes, tasks and layout files keep their previous version as `.bak`; a file that cannot be read is set
  aside as `.corrupt` and the backup is used instead of silently starting empty.
- Unhandled errors are logged. An error on the UI thread no longer closes the desk; a crash restores the taskbar.
- A missing WebView2 runtime now shows a message with a download link.
