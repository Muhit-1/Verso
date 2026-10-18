# Getting onto the desktop

The findings from Phase 0, on **Windows 11 Pro N for Workstations, build 26200**.

This is the one part of Verso that rests on undocumented behaviour, and the
one part most likely to break on a Tuesday. Everything here was measured, not
assumed — if the wallpaper stops appearing after a Windows update, start by
re-running the checks at the bottom.

## The headline

**The WorkerW technique does not work on this build.** Not "works with a
caveat" — it produces nothing visible, while reporting success at every step.

That is exactly what Phase 0 exists to find out, and it is why the plan says
to spend a weekend on it before a month on everything else.

## What Explorer's desktop actually looks like here

```
Progman  (0x203D2)
├── SHELLDLL_DefView          visible, 1920x1080, WS_EX_LAYERED
│   └── SysListView32         "FolderView" — the icons
└── WorkerW  (0x909F8)        visible, 1920x1080
```

There are also ten or so **top-level** `WorkerW` windows. All of them are
invisible, none owns a `SHELLDLL_DefView`, and none of them is the wallpaper.

## What was tried

### 1. The split message — does not fire

`SendMessageTimeout(Progman, 0x052C, …)` is supposed to make Explorer move
`SHELLDLL_DefView` into its own `WorkerW` and leave a second, visible
top-level `WorkerW` behind it for the wallpaper.

Tried with all four documented payloads — `wParam/lParam` of `0/0`, `0xD/0`,
`0xD/1`, `0xD/2` — each followed by a three-second wait:

| after | DefView owner | visible top-level WorkerW |
|---|---|---|
| baseline | `Progman` | 0 |
| `0x052C 0,0` | `Progman` | 0 |
| `0x052C 0xD,0` | `Progman` | 0 |
| `0x052C 0xD,1` | `Progman` | 0 |
| `0x052C 0xD,2` | `Progman` | 0 |

The desktop never splits.

### 2. Parenting into Progman's child WorkerW — succeeds, and is invisible

This is the trap. Attaching into `WorkerW (0x909F8)` reports every sign of
success:

- `SetParent` returns the previous parent with no error
- `GetAncestor(GA_PARENT)` confirms the new parent
- the window is `IsWindowVisible == true`, full-size, at 0,0
- it receives `WM_PAINT` four times a second and paints on every one

And nothing whatsoever appears on screen. Verified with a WPF window, and
again with a raw Win32 GDI window to rule out compositing — both paint,
neither is ever seen.

Making Explorer's icon list-view transparent does not help either: it already
is. `LVM_GETBKCOLOR` on `SysListView32` returns `CLR_NONE` before we touch it.

> **This is why `DesktopLayer.DetectLayout()` exists.** `Attach()` alone
> returns success here. Code that trusts it ships an invisible wallpaper.
> The reliable test is whether a **visible top-level `WorkerW`** exists after
> the split message — invisible ones are always present and mean nothing.

### 3. What does work — a bottom-most window we own

An ordinary top-level window, pinned to `HWND_BOTTOM`, sits above the desktop
and below every application window. Verified: it renders, it stays behind
Chrome and WhatsApp, and it survives Show Desktop.

Two further problems had to be solved to make it usable rather than merely
visible.

**WPF will not be layered.** Calling
`SetWindowLongPtr(GWL_EXSTYLE, … | WS_EX_LAYERED)` on a WPF `Window` returns
success, sets no error, and the flag never appears in the style;
`SetLayeredWindowAttributes` then fails with `ERROR_INVALID_PARAMETER` (87).
WPF owns that bit through `AllowsTransparency`, which uses
`UpdateLayeredWindow` — and that does not composite child HWNDs, so a WebView2
inside would not render at all.

The fix is `DesktopSurface`: Verso registers its own window class and creates
its own HWND with the extended styles set at creation, then hosts WebView2
through the Win32 API (`CreateCoreWebView2ControllerAsync(hwnd)`) rather than
the WPF control.

**A colour key cannot reach WebView2.** With `WS_EX_LAYERED` +
`LWA_COLORKEY` correctly applied — confirmed by reading back
`ex=0x8080080`, `LAYERED=True`, `key=0xFF00FF`, `flags=LWA_COLORKEY` — the
window still renders fully opaque. WebView2 presents through
DirectComposition, which DWM composites straight to the screen, so the colour
key never sees its output. Passing `--disable-gpu --disable-gpu-compositing`
does not change this.

## What Verso does

**A window region.** `SurfaceRegion` reshapes the surface to exactly the
rectangles the page reports it is drawing in. Pixels outside a window's region
are not part of the window: they are not painted, and mouse input at those
coordinates goes to whatever is underneath.

So the desktop stays entirely usable — the real wallpaper shows, the icons
show, clicks and drags reach Explorer — everywhere the desk is not. Verified
by hit-testing:

| point | window hit |
|---|---|
| over the task field | `Chrome_RenderWidgetHostHWND` → `VersoDesktopSurface` |
| over the dial panel | `Chrome_RenderWidgetHostHWND` → `VersoDesktopSurface` |
| over bare wallpaper | `SysListView32` → `Progman` |
| over the icon column | `SysListView32` → `Progman` |

`web/shell/js/regions.js` publishes the geometry; `DeskWindow.OnWebMessage`
validates and applies it. The region has to match the painted box exactly —
bleed exposes the page background as a hairline around every panel, shortfall
clips the panel border.

This also settles **Phase 2** without a mouse hook: the desk receives real
mouse and keyboard input because it is a real window, and everywhere else the
input was never ours to begin with. No `WH_MOUSE_LL`, and none of the
antivirus surface that comes with one.

One deliberate trade: the surface is **not** `WS_EX_NOACTIVATE`. A window that
never activates never takes keyboard focus, and the task field has to be
typable. Clicking the desk therefore raises it — so `ReassertPlacement` skips
the re-pin while the desk is the foreground window, and it drops back the
moment you click away.

## What this costs

Verso sits **over** the user's wallpaper rather than behind the icons. On a
build where `DetectLayout()` reports `Splittable`, `DeskMode.Wallpaper` still
takes the WorkerW route and Verso is the wallpaper properly. Everywhere else
it is an overlay.

For the widget half of the product that is no loss. For the *live wallpaper*
half it is a real one — video wallpaper needs the WorkerW, and on this build
there isn't one. See the open question in [plan.md](plan.md).

## Re-testing after a Windows update

```
dotnet run --project tools/Verso.Spike.WorkerW
```

It prints the layout, the strategy used, and whether the desktop icons ended
up in front. The `desktop` line is the one to paste into a bug report:

```
Progman=0x203D2  DefView-under-Progman=yes  sibling-WorkerW=none  Progman-child-WorkerW=0x909F8
```

`sibling-WorkerW=none` with `DefView-under-Progman=yes` is the signature of a
build where the technique is dead. If `sibling-WorkerW` ever becomes a handle,
the split is back and `DeskMode.Wallpaper` starts working again on its own.

---

## Update 2026-10-18 — a layer behind the icons does exist (build 26300)

Everything above stays true for the *WorkerW* routes. What was missing was one more
placement, found by painting a test window seven different ways and checking the
**composed screen** (`tools/Verso.Spike.Layers`), not Win32's return values:

> A **layered child of `Progman`** (`WS_CHILD | WS_EX_LAYERED`, alpha 255), inserted
> **directly behind `SHELLDLL_DefView`**, is visible with the desktop icons and taskbar
> drawn on top of it, and a WebView2 playing a 1080p mp4 inside it shows and animates.

Requirements, each measured:

- Progman must already have its extra `WorkerW` child. A freshly started Explorer does
  **not**; it appears when `0x052C` (`0xD`, `1`) is sent. Without it the same window is
  invisible (checked both ways). `DetectLayout()` always sent the message but only looked
  for a *top-level* WorkerW, so it called this build "not splittable" and never tried this.
- `WS_EX_LAYERED` is needed (`NOREDIRECTIONBITMAP`, or no style, or bottom-of-Progman
  placement: all invisible). The z-position behind DefView is needed too.
- The window is a child of Explorer's window, so it dies when Explorer restarts. Explorer
  broadcasts `TaskbarCreated` when it is back (received); rebuild then, incl. the WebView2.
- Progman resizes itself on a display change; the child does not.

In Verso this is `ProgmanLayer` / `ProgmanSurface` (`Verso.Interop`), used by
`DeskMode.Background`. The old click-through top-level background window covered the
icons and taskbar and has been removed. Layout value: `DesktopLayout.LayeredChild`.

Limitation by design: the icon layer is in front, so a wallpaper hosted here receives no
mouse input. Re-run the spike after Windows updates.
