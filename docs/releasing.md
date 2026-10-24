# Releasing

## Build an installer locally

```powershell
choco install innosetup        # once
pwsh installer/build.ps1 -Version 0.2.0
```

Result: `artifacts/installer/Verso-Setup-0.2.0.exe`. It is a per-user install (no administrator prompt), self-contained
(no .NET needed on the target PC) and offers "Start Verso when I sign in".

**Uninstall** stops Verso and then runs `Verso.exe --restore-desktop`, which puts the taskbar and desktop icons back —
a killed process cannot undo what it hid. User data in `%APPDATA%\Verso` is kept on purpose. Windows taskbar options
changed through Settings > Desktop (position, size, ...) are restored with the dashboard's "Restore how it was before
Verso" *before* uninstalling.

## Ship a release

1. Update `CHANGELOG.md`.
2. `git tag v0.2.0 && git push origin v0.2.0`.
3. The `Release` workflow runs the tests, builds the installer and attaches it to a GitHub release.

## Before the first public release

- [ ] Sign the installer and `Verso.exe` (Authenticode certificate, `signtool` step in `release.yml`). Unsigned builds
      get a SmartScreen warning.
- [ ] Walk the manual checklist below on a clean Windows 10 and a Windows 11 machine.
- [ ] Pick a licence (`LICENSE`) — the repository does not have one yet.

## Manual checklist

Things no test here can cover. Tick them on a real machine, per release.

- [ ] Fresh install on a PC without .NET: starts, tray icon appears, dashboard opens.
- [ ] Without the WebView2 runtime: a clear message with a download link, not a crash.
- [ ] Wallpaper (video, GIF, image, interactive) shows behind the icons and taskbar.
- [ ] `tools/probe-wallpaper` shows only PASS (see `docs/security.md`).
- [ ] Full-screen game or video: the desk pauses; leaving it resumes.
- [ ] Lock and unlock; sleep and wake; the wallpaper resumes.
- [ ] Restart Explorer (`taskkill /f /im explorer.exe`, then start it): the desk comes back.
- [ ] Change resolution and display scaling; unplug and plug a second monitor.
- [ ] End the Verso process in Task Manager, start it again: the taskbar and icons are as the settings say.
- [ ] Uninstall: taskbar and desktop icons are back, no `Verso.exe` left running.
