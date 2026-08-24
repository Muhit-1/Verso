# Verso — Windows tech stack and build plan

## What it is

**A live wallpaper that's also a widget desk.**

Your desktop background plays video, an image, or an interactive page. On
top of it sit small panels for the things you glance at all day: the time,
your task list, what's playing. You choose which ones you want and where
they go.

Widgets are ordinary web pages, so anyone can write one — including you,
in an afternoon.

*A verso is the left-hand page of an open leaf — one side of a
composition, which is exactly what a widget is.*

---

Plain-language plan. Windows only. Written for someone who can pick up any
language, building this for themselves first with the option to sell it
later.

> **Revision note.** This document was originally written under the working
> name *Verse*. The name is **Verso** — including `window.VersoHost` and the
> `verso.tasks.v1` storage key. Nine other amendments are marked
> **[amended]** where they appear and summarised at the end. Phases 0-2 are
> now built; where the plan's assumptions did not survive contact with
> Windows, the section says so.

---

## The one decision everything else hangs off

**Native shell + web widgets.**

You write a small native Windows program whose only job is to own a window
that sits behind the desktop icons. Inside that window you put a browser
control. All your widgets — clock, tasks, music — are HTML/CSS/JS running
inside that browser control.

Why this split and not "all native" or "all Electron":

- All native (WPF/WinUI widgets) gives you the best performance but every
  widget becomes a C# project. Nobody else will ever write a widget for
  you. You kill the marketplace before it exists.
- All Electron is the fastest to build but idles at 250–400 MB of RAM.
  For an app that runs 100% of the time your computer is on, that's the
  thing reviewers will beat you up over.
- Native shell + web widgets is what Wallpaper Engine and Lively both do.
  Small native footprint, and widgets are just web pages. This is the
  right answer.

The web layer is the widget layer. It already talks to a `window.VersoHost`
object and falls back to demo data when that object isn't there — so it runs
in a plain browser, in Lively today, and against your native host later,
with no changes.

---

## What to install

| | | |
|---|---|---|
| .NET 10 SDK | free | **[amended]** — was .NET 8 |
| WebView2 Runtime | free | Already on every Windows 10/11 machine — nothing to ship |
| Git | free | |
| Node | free | Only to run `tools/serve.mjs` and regenerate the icon. Not a build dependency |
| Visual Studio 2022 Community | free | *Optional.* `dotnet build` does everything. You want VS for the WPF designer and native debugging |

That's the whole toolchain. No paid anything until you decide to sell.

**[amended] .NET 10, not .NET 8.** .NET 8 leaves support in November 2026,
which lands mid-project. .NET 10 is the current LTS. The target framework
lives in one place — `VersoTargetFramework` in `Directory.Build.props` — so
if a dependency ever objects it is a one-line change back.

---

## The libraries

| Job | Use | Notes |
|---|---|---|
| App framework | **.NET 10 + WPF** | WinUI 3 is newer but its window-handle story is worse, and you need raw window handles constantly here |
| Browser control | **WebView2** (`Microsoft.Web.WebView2`) | Chromium, ships with Windows, no runtime to bundle |
| Video wallpaper | **libmpv, called directly** | **[amended]** — see below |
| Win32 calls | **CsWin32** (`Microsoft.Windows.CsWin32`) | Source generator — you list the API names in `NativeMethods.txt` and it writes correct P/Invoke signatures for you. Saves real hours |
| Now playing | **`Windows.Media.Control`** (WinRT) | `GlobalSystemMediaTransportControlsSessionManager`. Reads title/artist/artwork/position from Spotify, YouTube Music, browser tabs, anything. Also gives you play/pause/next. Built into Windows — you get the whole music widget nearly free |
| Tray icon | **H.NotifyIcon** | Because the built-in one is from 2005 |
| Settings storage | plain **JSON** on disk | Do not add a database. It's a settings file |
| Installer | **Inno Setup** | Free, simple, produces one .exe |
| Autostart | Registry `Run` key | Three lines. Don't use Task Scheduler |

**[amended] libmpv directly, not `Mpv.NET`.** `Mpv.NET` is effectively
unmaintained. mpv's C API is tiny — `mpv_create`, `mpv_set_option_string`,
`mpv_command`, `mpv_terminate_destroy` covers nearly all of it — so budget
roughly 200 lines of P/Invoke you own outright. A dead dependency sitting in
your render path is worse than code you control.

---

## How the pieces fit

```
  ┌─────────────────────────────────────────────────┐
  │  Tray app (WPF)                                 │
  │   settings window, wallpaper picker, quit       │
  └───────────────────┬─────────────────────────────┘
                      │
  ┌───────────────────▼─────────────────────────────┐
  │  Wallpaper host window                          │
  │   - finds/creates WorkerW, SetParent into it    │
  │   - one window per monitor                      │
  │   - forwards mouse input from the desktop       │
  │   - pauses everything when a fullscreen app     │
  │     is focused, on battery, or when locked      │
  └────────┬──────────────────────────┬─────────────┘
           │                          │
  ┌────────▼────────┐        ┌────────▼─────────────┐
  │  libmpv surface │        │  WebView2            │
  │  video/gif      │        │  widget layer        │
  │  wallpaper      │        │  (web/shell)         │
  └─────────────────┘        └────────┬─────────────┘
                                      │ host bridge
                             ┌────────▼─────────────┐
                             │  VersoHost object    │
                             │  tasks   → JSON file │
                             │  music   → SMTC API  │
                             │  system  → WMI/perf  │
                             └──────────────────────┘
```

The bridge is `CoreWebView2.AddHostObjectToScript("VersoHost", obj)`.
Your C# object appears in JavaScript as `window.VersoHost`. That's it —
that's the whole integration. Details and the three-layer reasoning are in
[host-api.md](host-api.md).

---

## The three things that will actually cost you time

Everything else is ordinary app work. These are not.

### 1. Getting a window behind the desktop icons

The plan: send message `0x052C` to `Progman`, which makes Explorer spawn a
`WorkerW`; find it and `SetParent` your window into it. Roughly 50 lines,
undocumented, and Windows updates can break it.

**They did.** On Windows 11 build 26200 the desktop never splits, and
attaching into Progman's own child `WorkerW` reports success at every step
while drawing nothing. This cost most of Phase 0 to establish and is the
single most important thing in this repo to know:

> A window can be parented into the `WorkerW`, report `IsWindowVisible`,
> be full-size, receive `WM_PAINT` four times a second, paint on every
> one — and never appear on screen.

`DesktopLayer.DetectLayout()` exists solely so no future code trusts
`Attach()` on its own. Verso now uses a bottom-most window it owns, reshaped
by a window region. Full measurements and the two dead ends (WPF refusing
`WS_EX_LAYERED`; colour keys not reaching WebView2's DirectComposition
output) are in [desktop-layer.md](desktop-layer.md).

*Built:* `Verso.Interop/DesktopLayer.cs`, `DesktopSurface.cs`,
`SurfaceRegion.cs`, `BottomMostLayer.cs`.

### 2. Making widgets clickable

This is the hard one. A window behind the icon layer doesn't receive mouse
events — Explorer swallows them.

**[amended] Decide between two approaches before writing a line.**

**(a) Low-level mouse hook.** What Lively does. Install `WH_MOUSE_LL`, work
out whether the cursor is over the desktop (and not over an icon or another
window), forward the event into your WebView2. Widgets sit genuinely behind
the icons. Budget a week.

**(b) A separate layered widget window.** Keep the *wallpaper* in `WorkerW`,
but put the *widget desk* in its own `WS_EX_LAYERED | WS_EX_NOACTIVATE`
window above the icon layer, sized to the widgets. It receives its own mouse
input, so there is no hook at all. Budget two days.

The cost of (b) is that widgets float above desktop icons instead of behind
them. The benefit is that it removes both the week of work *and* the single
biggest antivirus-false-positive risk in the app — a global mouse hook plus
`SetParent` into another process is, structurally, exactly what malware
does.

The original plan assumed (a) without stating it was a choice.

**Resolved: neither, in the end.** (b) was chosen — and once the desk became
a window reshaped to its own panels, the problem dissolved. The desk is a
real window, so it receives real mouse *and keyboard* input; outside the
region, the input was never ours to intercept. Explorer keeps it.

No `WH_MOUSE_LL`, no synthetic event forwarding, and none of the antivirus
surface that comes with a global hook. Two days, not a week.

One trade worth knowing: the surface is deliberately **not**
`WS_EX_NOACTIVATE`. A window that never activates never takes keyboard focus,
and the task field has to be typable — measured: with the flag set, clicks
work and typing silently does nothing. So clicking the desk raises it, and
the re-pin is skipped while the desk is foreground.

### 3. Not being a battery hog

Non-negotiable, and the reason most hobby versions of this get uninstalled:

- Pause rendering when any window is maximised or fullscreen
- Pause on battery below a threshold
- Pause when the session is locked or the monitor is off
- Throttle the WebView2 when it's not visible

Get this wrong and you have a 12% background CPU load forever. Get it right
and people forget it's running, which is the goal.

**[amended] The two cheap ones move to Phase 1.** Pause-on-fullscreen
(`SHQueryUserNotificationState`) and pause-on-lock (WTS session
notifications) are about thirty lines each. You will be running this on your
own machine daily from Phase 1 onward — months of unthrottled WebView2 plus
mpv is self-inflicted. The rest stays in Phase 4.

---

## Two annoyances to know about now, not later

**Antivirus false positives.** `SetParent` into another process plus a
global mouse hook is, structurally, exactly what malware does. Windows
Defender and especially the smaller AV vendors will flag you. The fix is a
code-signing certificate (~$200–400/year) and building reputation with
SmartScreen over time. Plan for it if you sell. Note that choosing approach
(b) above removes half of this problem.

**The WorkerW technique is undocumented.** Microsoft never promised it
works. It has survived a decade, but it's the one thing in your app that
could break on a Tuesday. `DesktopLayer.DescribeDesktop()` exists so that
when it does, you can see what changed in one line.

---

## The plan

### Phase 0 — prove the hard part ✅ *done, and it failed*

```
dotnet run --project tools/Verso.Spike.WorkerW
```

**Result on this machine (Windows 11 build 26200): the WorkerW technique does
not work.** Explorer never splits the desktop, and a window parented into
Progman's child `WorkerW` reports success at every step — visible, full-size,
receiving `WM_PAINT` — while drawing nothing on screen.

This is Phase 0 doing its job. The full measurements are in
[desktop-layer.md](desktop-layer.md).

The consequence: Verso uses a **bottom-most window it owns**, reshaped by a
window region to exactly the rectangles it draws in. The desktop stays fully
usable everywhere the desk is not. `DesktopLayer.DetectLayout()` still checks
for a splittable desktop and takes the WorkerW route where one exists, so
older builds get the real thing.

### Phase 1 — on the desktop ✅ *done, except video*

- `DesktopSurface` — our own HWND, because WPF refuses `WS_EX_LAYERED`
- WebView2 hosted through the Win32 API, showing `web/shell/index.html`
- tray icon: hide/show the desk, reload, open the data and theme folders, exit
- `PerMonitorV2` DPI awareness in the manifest
- `PowerGate` — pause on fullscreen, on lock, on monitor-off, on low battery

**Not done: video wallpaper.** `libmpv` needs `libmpv-2.dll`, a ~40 MB binary
that is not in the repo and that I did not download unprompted. It also wants
the WorkerW to be useful, and on this build there isn't one — a full-screen
video overlay would cover the desktop icons. Worth deciding deliberately
rather than building on the assumption.

At the end of this you are running your own desk.

### Phase 2 — make it clickable ✅ *done*

Decided: the **layered window**, not the mouse hook. And in the end the window
region made even that straightforward — the desk is a real window, so it gets
real mouse and keyboard input, and everywhere outside the region the input was
never ours to intercept.

**No `WH_MOUSE_LL`, and none of the antivirus surface that comes with one.**

Verified end to end: click the task field on the desktop, type, press Enter,
and it lands in `%APPDATA%/Verso/tasks.json` through the bridge.

*"When you can tick a task off your desktop, the product exists."*

### Phase 3 — real data (2–4 days) ← **next**

Wire the bridge: tasks to a JSON file *(done)*, music to the SMTC
API. The demo fallback stays in the file — it's what makes the web layer
runnable in a plain browser, and deleting it would end your ability to
develop widgets without launching the app.

### Phase 4 — don't be a hog (3–4 days)

The remaining pause rules: battery, monitor off, throttle-when-hidden.
Multi-monitor: one window per display, correct DPI, handle monitor hot-plug.
Tedious, unglamorous, load-bearing.

**Stop here if it's just for you. Total: roughly 3–5 weeks of evenings and
weekends.**

### Phase 5 — could ship it (+6–10 weeks)

Settings window (pick wallpaper, arrange widgets, per-monitor config, theme
picker). The widget loader that renders `web/widgets/*` into the desk.
Drag-and-drop layout saved to JSON. Installer. Autostart. Crash reporting.
First-run experience.

**[amended]** The widget manifest spec is *already written* —
[widget-spec.md](widget-spec.md) — so widgets built between now and here
won't need rewriting.

### Phase 6 — could sell it (+ open-ended)

Licensing, auto-update, a widget gallery, and — the real work — support.

---

## One thing to hold onto

Keep the boundary between host and widgets absolutely clean. Every widget
is a folder with an `index.html` and a small manifest, and it may only
reach the outside world through `window.VersoHost`. Never let a widget call
into .NET directly, never let one widget's CSS touch another's.

Two reasons. It's how you eventually port to Mac and Linux by rewriting
only the host. And it's the only version of this that has a business in it,
because it's the only version where other people can write widgets for you.

The wallpaper is what gets people to install it. The widget system is what
makes it worth money.

**[amended] There is a third boundary the original plan didn't have: the
theme.** Widgets and themes are different contracts. A widget *reads* theme
tokens; a theme never knows a widget's name. Keeping them in separate
folders with separate specs from day one is what makes "add any theme later"
actually true rather than aspirational. See [theme-spec.md](theme-spec.md).

---

## Summary of amendments

| # | | |
|---|---|---|
| 1 | Name | `Verse` → **`Verso`** everywhere, including the host object and storage keys |
| 2 | Framework | .NET 8 → **.NET 10 LTS**; TFM isolated in `Directory.Build.props` |
| 3 | Video | `Mpv.NET` → **libmpv called directly**, ~200 lines you own |
| 4 | Input | Phase 2 now **decides** between the mouse hook and a layered widget window, rather than assuming the hook |
| 5 | Power | Pause-on-fullscreen and pause-on-lock move from Phase 4 to **Phase 1** |
| 6 | DPI | `PerMonitorV2` set **day one**, not at Phase 4 |
| 7 | Specs | Widget manifest **and a theme spec** written now, not at Phase 5 |
| 8 | **Desktop layer** | The WorkerW technique is **dead on Windows 11 build 26200**. Verso uses a bottom-most window it owns, reshaped by a window region. See [desktop-layer.md](desktop-layer.md) |
| 9 | **Input** | Phase 2 needed no mouse hook at all — a window region gives real input inside the desk and true click-through outside it |
| 10 | Video | libmpv deferred: it needs a binary that is not in the repo, and without a WorkerW a full-screen video would cover the desktop icons |
