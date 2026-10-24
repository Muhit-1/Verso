# Windows cases that need a real machine

The first version was built and used on one PC (Windows 11, one 1080p monitor, 100% scaling). These are the cases
the code is written to handle but that nobody has watched happen. Tick each one and note the Windows build.

Changes made on reading the code, not on a machine that shows the failure — confirm each:

| Case | What the code does now | Check |
|---|---|---|
| Monitor plugged or unplugged | `DeskController` diffs windows against `Monitors.All()`; the event is now marshalled to the UI thread (it arrives on a background thread) and errors are logged | Plug a second display in and out; desk appears and goes away without a restart |
| Resolution or scaling change | Windows are keyed by monitor bounds; a resolution change recreates them | Change resolution; change scaling 100% -> 150% -> 100%. At 150% check text sharpness and that widgets keep their places |
| Sleep and wake | `PowerGate.Resumed` triggers a reconcile 2.5 s after waking | Sleep, wake: wallpaper is back, widgets respond, mouse works |
| Lock and unlock | Pauses on lock, resumes on unlock | Lock for a minute, unlock |
| Explorer restart | `ShellRestartWatcher` rebuilds the Progman layer | `taskkill /f /im explorer.exe` then `explorer.exe` |
| Taskbar moved, resized, auto-hidden, or the compact dock turned on | The usable area is re-read every 3 s; a new value seen twice reloads the desk page with it | Move the taskbar to the left; widgets stay clear of it |
| Remote Desktop in and out | Same path as a display change | Connect and disconnect |
| Windows 10 | Manifest allows 1809+; the Progman trick was only measured on Windows 11 build 26300 (`docs/desktop-layer.md`) | Run `tools/Verso.Spike.Layers` on a Windows 10 machine; if the layer is missing the app says so in the tray tooltip instead of covering the icons |
| Elevated windows | The mouse mirror uses Raw Input, which sees all input | Click on the desktop while an administrator app has focus |
| WebView2 runtime missing | A message with a download link | Uninstall the runtime in a VM and start Verso |

Not handled, on purpose or not yet:

- Per-monitor wallpapers (`DisplaySettings` exists in settings, nothing reads it).
- A maximised window on one monitor pauses all of them (see `docs/performance.md`).
- Mixed-DPI monitors: the manifest asks for PerMonitorV2, but whether a window parented into Progman receives
  `WM_DPICHANGED` when dragged between monitors is unproven.
