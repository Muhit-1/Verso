# Performance

A wallpaper runs all day, so its cost is a feature. This is what already keeps it cheap, what is not done yet, and
how to measure.

## What already keeps it cheap

- The desk **stops drawing** when it cannot be seen (`Power/PowerGate.cs`): a full-screen app, a maximised window in
  front, the session locked, the monitor off, low battery, or "Hide the desk" from the tray. Hidden windows are also
  suspended (`TrySuspendAsync`), which lets Chromium give its memory back.
- Fireflies run at 15 fps on a half-resolution canvas with one pre-rendered sprite per colour.
- Mouse input is mirrored with Raw Input into a stack buffer: no hook, no allocation per move.
- While paused, the three-second placement timer and the usable-area check do nothing.

## Not done yet

- **A maximised window pauses every monitor.** The rule is "the foreground window is maximised", not "a maximised
  window covers *this* monitor", so with two monitors a maximised browser on one stops the wallpaper on the other.
  Fix: make `DesktopState` report which monitors are covered and pause per window.
- **`PowerSettings.MaxFps` is not used.** The setting exists (default 30) but nothing reads it. Either apply it to
  the widgets that animate, or remove it so it is not promised.
- **Warm windows stay alive.** The text editor and the app picker are built off-screen at start and kept, each with
  its own WebView2 renderer. That makes them open instantly and costs idle memory. Measure it (below); if it is
  large, build them on first use instead.
- **No numbers yet.** Nothing in the repository records CPU, GPU or memory for any state.

## Measure

```powershell
pwsh tools/perf-sample.ps1 -Seconds 60 -Label "video wallpaper, idle"
```

It adds up Verso and every process it started and prints average and peak CPU and memory. GPU: add the "GPU" columns
in Task Manager > Details for the same processes. Fill the table, on the machine you care about, before and after any
change that is meant to help:

| State | CPU avg | CPU peak | Memory | GPU |
|---|---|---|---|---|
| Widgets only, no wallpaper | | | | |
| Video wallpaper + widgets | | | | |
| Interactive wallpaper + widgets | | | | |
| Maximised window in front | should be ~0 | | | |
| Full-screen game | should be ~0 | | | |
| Two monitors, one maximised | | | | |

For startup time, run with `VERSO_TRACE=1` and read `%APPDATA%\Verso\trace.log`.
