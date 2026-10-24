<h1>Verso</h1>

**A live wallpaper that's also a widget desk.**

Your desktop background plays a video, a GIF, an image or an interactive page,
*behind* your icons and taskbar. On top of it sit small glass widgets for the
things you glance at all day: the clock, what's playing, a focus timer, sticky
notes, boxes of your favourite apps.

Everything on the desk is web technology (WebView2), so the look is easy to
change and the widgets are easy to write.

> *A verso is the left-hand page of an open leaf — one side of a
> composition, which is exactly what a widget is.*

Windows only, for now. The architecture keeps every Windows-specific thing in
two projects, so reaching macOS or Linux means rewriting those two.

---

## Status

Built and used on **one machine**: Windows 11 build 26300, one 1080p monitor.
The honest list of what has and has not been tried is kept in the project
notes; the short version is below.

| | |
|---|---|
| Live wallpaper behind icons and taskbar (video, GIF, image, HTML) | working |
| Pauses itself for full-screen apps, maximised windows, lock, battery | working |
| Survives Explorer restarts and resolution changes | working |
| Outpost desk: clock, now playing (Windows media session), Pomodoro, stopwatch, fireflies | working |
| Sticky notes, typed in a small pop-up editor window | working |
| App launcher boxes, installed-apps picker, drag a desktop icon onto a box | working |
| Dashboard: Wallpaper, Widgets, Apps, Settings (Desktop, Theme) | working |
| Windows taskbar options (position, alignment, size, auto-hide, ...) from the dashboard | working |
| Compact dock: a small floating dock with only the apps that are open | working, new |
| The original ("classic") desk with the theme and widget folders | working, kept |
| Second monitor, DPI other than 100%, sleep/wake | **not tried yet** |
| Installer (per-user, self-contained) | written, **not yet run on Windows** |
| Tests and CI | storage, sanitising, launch/navigation rules, web manifests and CSP; CI on every push |

**How it gets behind the icons.** The old WorkerW trick does not work on
current Windows 11. Verso parents a layered window into `Progman`, directly
behind the icon layer, after asking Explorer to create the extra `WorkerW`
(`0x052C`). Mouse input is mirrored into the page with Raw Input, because the
icon layer sits in front of the wallpaper. Measurements, dead ends and how to
re-test after a Windows update: [docs/desktop-layer.md](docs/desktop-layer.md).

---

## Try it

```bash
dotnet run --project src/Verso.App
```

A tray icon appears, the desk lands on your desktop and the dashboard opens.
Pick a wallpaper in **Wallpaper**, switch widgets on in **Widgets**, add apps
in **Apps**.

On the desk, the round **!** button opens a small menu: arrange (move and
resize widgets), switch palette, switch widgets on and off, new sticky note,
new app box, hide the taskbar or desktop icons, open the dashboard.

For daily use, build Release (starts noticeably faster than Debug):

```bash
dotnet publish src/Verso.App -c Release -r win-x64 --self-contained false -p:PublishReadyToRun=true -o artifacts/publish/Verso
```

**The web layer alone**, in a plain browser — no .NET needed:

```bash
node tools/serve.mjs
```

Then open <http://localhost:5173/shell/index.html> (classic desk) or
`/dashboard/index.html`. They run on demo data, which is the point: the web
side can be built without the host existing.

**Tests**

```bash
npm test                                  # web layer: manifests, links, CSP — Node only
dotnet test tests/Verso.App.Tests         # host logic — needs Windows
```

CI runs both ([.github/workflows/ci.yml](.github/workflows/ci.yml)). Building the installer and shipping a release:
[docs/releasing.md](docs/releasing.md). How untrusted wallpapers and widgets are kept away from the host:
[docs/security.md](docs/security.md).

**Spikes** — re-run after a Windows feature update:

```bash
dotnet run --project tools/Verso.Spike.WorkerW
dotnet run --project tools/Verso.Spike.Layers -- --web --out spike-out
```

---

## What you need

| | |
|---|---|
| .NET 10 SDK | `dotnet --list-sdks` |
| WebView2 Runtime | already on every Windows 10/11 machine |
| Node | only for the dev server and the icon generator |
| Git | |

Everything else comes from NuGet on first build.

---

## The dashboard

| Page | What it does |
|---|---|
| **Wallpaper** | Choose or clear the video, GIF, image or interactive page. |
| **Widgets** | One card and one switch per widget. Switching one never touches another. |
| **Apps** | Your app library, grouped into boxes. New apps land in *Recent*; move them into your own boxes. The same boxes appear on the desk. |
| **Settings > Desktop** | Hide taskbar / desktop icons; the **Compact dock**; Windows' own taskbar options (position, alignment, size, auto-hide, badges, combine buttons, Search, Task view) with Undo and "Restore how it was before Verso". |
| **Settings > Theme** | The desk's colour palettes and the dashboard's own look. |

The dashboard works offline: its fonts are bundled (`web/dashboard/fonts/`).

### The compact dock

Windows' full-width taskbar is hidden and a small pill shows **only the apps
that are open right now**, as wide as their icons. It appears with the first
open app and disappears after the last. Click an icon to bring the app
forward, click the front app to minimise it, click again to restore. Hold the
pointer against the screen edge to slide the real taskbar in for a moment
(clock, tray, notifications). It hides itself for full-screen apps.

What it does not have: badges, flashing, jump lists, pinned-but-closed apps.
Turn it off in Settings > Desktop to get the Windows taskbar back.

---

## Layout

```
verso/
├── src/
│   ├── Verso.Core/        contracts and plain data — no Win32, no WPF
│   ├── Verso.Interop/     every Win32 call, generated by CsWin32
│   │                        ProgmanSurface / ProgmanLayer  the wallpaper window behind the icons
│   │                        InputMirror     Raw Input -> the page, without a hook
│   │                        DesktopChrome   hide the taskbar / desktop icons
│   │                        TaskbarSettings Windows taskbar options (registry + shell API)
│   └── Verso.App/         tray app, desk windows, dashboard window, the bridges
│       ├── Hosting/         ScriptBridge, DashboardBridge, LauncherLibrary, NotesLibrary,
│       │                    InstalledApps, ShellIcons, PickerBridge
│       ├── Windows/         DeskWindow, DashboardWindow, TextEditorWindow, AppPickerWindow
│       └── Dock/            the compact dock
├── tests/
│   ├── Verso.App.Tests/   xunit: storage, sanitising, launch and navigation rules
│   └── web/               node --test: manifests, links, Content-Security-Policy
├── installer/             Inno Setup script and build.ps1
├── tools/
│   ├── probe-wallpaper/   a wallpaper that reports whether it can reach the host
│   ├── Verso.Spike.WorkerW/   Phase 0: the desktop-layer experiment
│   ├── Verso.Spike.Layers/    pixel-measuring spike for the wallpaper window
│   └── serve.mjs              static server for the web layer
├── web/
│   ├── outpost/           the desk: framework, layout engine, widgets, round menu
│   ├── dashboard/         the dashboard, its themes and bundled fonts
│   ├── editor/            the pop-up text editor for notes and names
│   ├── picker/            the installed-apps picker
│   ├── shell/ themes/ widgets/   the classic desk, its themes and widgets
│   └── live wallpaper/    a sample interactive wallpaper
├── docs/                  plan, desktop layer, host API, theme and widget specs
├── design/                the mockups each feature was approved from
└── assets/                app icon, and the script that generates it
```

**`Verso.App` and `Verso.Interop` are the only projects that would need
rewriting to reach macOS or Linux.**

User data lives in `%APPDATA%\Verso\`: `settings.json`, `outpost.json`
(widget positions), `outpost-notes.json`, `shortcuts\` (Verso's own copies of
shortcuts you moved into a box), and the folders `themes\`, `widgets\` and
`dashboard-themes\` for installing your own without touching the app.

---

## Widgets

The Outpost desk is built on one rule: **every widget is independent.** A
widget is a self-contained module (`web/outpost/js/widgets/*.js`) with its own
element, its own saved state, its own position and its own on/off switch. It
never reads or writes another widget. The framework starts and stops each one
inside its own try/catch, so one failing cannot stop the others. See
`web/outpost/js/framework.js`.

| Widget | |
|---|---|
| Clock | Big clock and date; click AM/PM to switch 12/24 hour. |
| Now playing | What Windows is playing, with play/pause/skip. |
| Pomodoro, Stopwatch | Separate cards; they sit side by side by default. |
| Sticky notes | As many as you like, six colours, resizable, typed in a real pop-up window. |
| App launcher | See-through boxes of apps; drag a tile between boxes, or out onto the desktop. |
| Fireflies | Ambience, with its own switch. |

**Desktop shortcuts move, they are not copied.** Dropping a desktop icon on a
box moves the shortcut into Verso's own folder (the icon leaves the desktop);
dragging a tile out onto the bare desktop puts it back. Both have an Undo.

The **classic desk** (`web/shell`, `web/themes`, `web/widgets`) keeps the
original theme and widget-folder contract; see
[docs/theme-spec.md](docs/theme-spec.md) and
[docs/widget-spec.md](docs/widget-spec.md).

---

## Things worth knowing

- **Never serve the web layer from a `.local` host name.** Windows treats it as
  multicast DNS and every page load waited about two seconds. The virtual
  hosts use the reserved `.example` suffix (`verso.example`, ...).
- Web files are copied into the build output, so web edits need a rebuild.
- The application log is `%APPDATA%\Verso\logs\verso-YYYYMMDD.log` (tray: *Open logs folder*). If a settings or
  notes file is damaged, it is renamed `*.corrupt` and the last good copy `*.bak` is used.
- `VERSO_TRACE=1` writes a startup timeline to `%APPDATA%\Verso\trace.log`.
- For tests, start Verso with
  `WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=9222` and drive
  the pages through the DevTools protocol. Do not ship with that set.
- A maximised window in front pauses the desk (and its page load) on purpose.

---

## The one thing to hold onto

Keep the boundary between host and web layer absolutely clean. It's how this
eventually reaches Mac and Linux by rewriting only the host, and it's what lets
other people write widgets and themes.

---

## Regenerating the app icon

```bash
node assets/make-icon.mjs
```

Writes `assets/verso.png`, `assets/verso.ico` and `web/shell/favicon.png`.
No dependencies — the PNG encoder is about forty lines over Node's built-in
zlib.
