using System.Drawing;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Verso.App.Hosting;
using Verso.App.Power;
using Verso.App.Storage;
using Verso.App.Windows;
using Verso.Core.Contracts;
using Verso.Interop;

namespace Verso.App;

/// <summary>
/// Owns the desk windows and everything that decides when they draw.
/// </summary>
/// <remarks>
/// <para>
/// One panel window per physical monitor, kept in sync with
/// <see cref="SystemEvents.DisplaySettingsChanged"/> — plugging in or
/// unplugging a monitor, or changing resolution/arrangement, adds or
/// removes desk windows rather than requiring a restart.
/// </para>
/// <para>
/// On builds where <see cref="DesktopLayer.DesktopLayout.Splittable"/> is
/// not available (overlay mode — the common case), a second
/// <see cref="DeskMode.Background"/> window is created per monitor whenever a
/// background that needs the web stage (video, gif, or interactive) is
/// actually set — see <see cref="ReconcileAsync"/>. It lives in a
/// <see cref="ProgmanSurface"/>: a layered child of Progman directly behind
/// the desktop icons, so the icons and taskbar stay visible on top of it. If
/// that layer is not available the live wallpaper is simply not shown — the
/// desktop is left alone rather than ever being covered. In wallpaper mode
/// nothing extra is needed: that single window already renders the stage
/// itself.
/// </para>
/// <para>
/// A Progman child dies with Explorer, so <see cref="ShellRestartWatcher"/>
/// triggers a rebuild when the shell comes back.
/// </para>
/// </remarks>
internal sealed class DeskController : IDisposable
{
    private readonly ScriptBridge _bridge;
    private readonly SettingsStore _settings;
    private readonly PowerGate _power;
    private readonly Dictionary<Rectangle, DeskWindow> _panelWindows = [];
    private readonly Dictionary<Rectangle, DeskWindow> _backgroundWindows = [];
    private readonly DispatcherTimer _reassert;

    private ShellRestartWatcher? _shellWatcher;
    private string? _backgroundNote;
    private DesktopLayer.DesktopLayout _layout;
    private bool _started;
    private bool _reconciling;
    private bool _reconcilePending;

    public DeskController(ScriptBridge bridge, SettingsStore settings)
    {
        _bridge = bridge;
        _settings = settings;
        _power = new PowerGate(settings.Current.Power);
        _power.Changed += OnPauseChanged;

        // Activating another app, showing the desktop, or a resolution change can
        // all raise a bottom-most window. Re-asserting on a slow timer is far
        // cheaper than hooking foreground changes, and three seconds of being in
        // the wrong place is not something anyone will see.
        _reassert = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _reassert.Tick += (_, _) =>
        {
            // Background first, then panels — PinToBottom moves whichever is
            // pinned last to the very bottom, so this order keeps the panel
            // window just above the background window, exactly where it
            // needs to be for the panels to actually be visible over it.
            foreach (var window in _backgroundWindows.Values)
            {
                window.ReassertPlacement();
            }
            foreach (var window in _panelWindows.Values)
            {
                window.ReassertPlacement();
            }

            // Backstop for a missed TaskbarCreated broadcast: a wallpaper window
            // whose Explorer has gone is rebuilt here instead.
            if (_started && _backgroundWindows.Values.Any(w => !w.IsSurfaceAlive))
            {
                _ = ReconcileAsync();
            }
        };
    }

    /// <summary>What happened when the desk was placed. Shown in the tray tooltip.</summary>
    public string Status { get; private set; } = "starting";

    public bool IsShown { get; private set; }

    public async Task StartAsync()
    {
        // Done once, at startup: it sends a message to Explorer, and the answer
        // does not change while the session is running — including across a
        // hot-plug, which is why ReconcileAsync() reuses this rather than
        // re-detecting it on every monitor or background change.
        _layout = DesktopLayer.DetectLayout();

        await ReconcileAsync();

        IsShown = true;
        _started = true;

        _reassert.Start();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        _shellWatcher = new ShellRestartWatcher();
        _shellWatcher.ShellRestarted += OnShellRestarted;

        await _power.StartAsync();
    }

    /// <summary>
    /// Debounced entry point for anything that can change which desk windows
    /// should exist: a monitor being added/removed/resized, or the
    /// background setting changing. A monitor change settling, or two calls
    /// arriving close together, can happen before the first reconcile's
    /// <c>await</c>s finish — rather than let a second call run concurrently
    /// against the same dictionaries, it marks the pass as needing to happen
    /// again and the loop below repeats until nothing changed since the
    /// last time.
    /// </summary>
    public async Task ReconcileAsync()
    {
        if (_reconciling)
        {
            _reconcilePending = true;
            return;
        }

        _reconciling = true;
        try
        {
            do
            {
                _reconcilePending = false;
                await ReconcileOnceAsync();
            }
            while (_reconcilePending);
        }
        finally
        {
            _reconciling = false;
        }
    }

    /// <summary>
    /// Creates or disposes panel and background windows so both dictionaries
    /// match, right now, what the current monitors and background setting
    /// call for. Keyed by bounds rather than a stable monitor id — Windows
    /// hands out no such id through <see cref="Monitors.All"/>, and the
    /// bounds already change whenever anything about a monitor that would
    /// matter here does: resolution, arrangement, or it disappearing
    /// entirely.
    /// </summary>
    private async Task ReconcileOnceAsync()
    {
        var mode = _layout == DesktopLayer.DesktopLayout.Splittable
            ? DeskMode.Wallpaper
            : DeskMode.Overlay;

        var current = new HashSet<Rectangle>(Displays.All());

        foreach (var gone in _panelWindows.Keys.Except(current).ToList())
        {
            _panelWindows[gone].Dispose();
            _panelWindows.Remove(gone);
        }

        foreach (var added in current.Except(_panelWindows.Keys))
        {
            var window = new DeskWindow(_bridge, _settings, mode, _layout, added);
            _panelWindows[added] = window;
            await window.InitialiseAsync();
        }

        // Only in overlay mode: wallpaper mode's single panel window above
        // already renders the stage itself, full-screen, with no separate
        // surface needed. And only when there is actually something for the
        // web stage to show — "image" goes straight to the real Windows
        // wallpaper (DesktopWallpaper.SetImage), and "none" has nothing to
        // draw, so a window would just be a cost with nothing to show for it.
        var needsBackgroundWindow = mode == DeskMode.Overlay
            && _settings.Current.Background.Kind is "video" or "gif" or "interactive";

        _backgroundNote = null;
        var desiredBackgroundMonitors = new HashSet<Rectangle>();
        if (needsBackgroundWindow)
        {
            // Asks Explorer for Progman's WorkerW child — without it the layer
            // below is invisible, and a fresh Explorer never has one. If it
            // does not appear, show nothing: never fall back to a window that
            // would cover the icons.
            if (await ProgmanLayer.PrepareAsync())
            {
                desiredBackgroundMonitors = current;
            }
            else
            {
                _backgroundNote = "live wallpaper unavailable (desktop layer not ready)";
            }
        }

        // Gone monitors, a changed background, and windows whose Explorer was
        // restarted underneath them (a Progman child dies with Progman).
        foreach (var gone in _backgroundWindows
                     .Where(pair => !desiredBackgroundMonitors.Contains(pair.Key) || !pair.Value.IsSurfaceAlive)
                     .Select(pair => pair.Key)
                     .ToList())
        {
            _backgroundWindows[gone].Dispose();
            _backgroundWindows.Remove(gone);
        }

        foreach (var added in desiredBackgroundMonitors.Except(_backgroundWindows.Keys))
        {
            try
            {
                var window = new DeskWindow(_bridge, _settings, DeskMode.Background, _layout, added);
                _backgroundWindows[added] = window;
                await window.InitialiseAsync();
            }
            catch (InvalidOperationException ex)
            {
                _backgroundNote = $"live wallpaper unavailable ({ex.Message})";
            }
        }

        Status = _panelWindows.Count == 0
            ? "no displays"
            : $"{_panelWindows.Count} display(s) · {_panelWindows.Values.First().PlacementDescription}"
                + (_backgroundWindows.Count > 0 ? $" · {_backgroundWindows.Count} live wallpaper surface(s)" : "")
                + (_backgroundNote is null ? "" : $" · {_backgroundNote}");
    }

    private async void OnShellRestarted()
    {
        if (!_started)
        {
            return;
        }

        // Let the new shell finish building the desktop before touching it.
        await Task.Delay(600);
        await ReconcileAsync();
    }

    private async void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (!_started)
        {
            return;
        }

        await ReconcileAsync();
    }

    private async void OnPauseChanged(object? sender, PauseReason reason)
    {
        var render = reason == PauseReason.None;
        foreach (var window in _panelWindows.Values)
        {
            await window.SetRenderingAsync(render);
        }
        foreach (var window in _backgroundWindows.Values)
        {
            await window.SetRenderingAsync(render);
        }
    }

    /// <summary>Hides or shows the desk on the user's request from the tray.</summary>
    public void SetUserPaused(bool paused)
    {
        IsShown = !paused;
        _power.SetUserPaused(paused);
    }

    public void ReloadWeb()
    {
        foreach (var window in _panelWindows.Values)
        {
            window.ReloadWeb();
        }
        foreach (var window in _backgroundWindows.Values)
        {
            window.ReloadWeb();
        }
    }

    /// <summary>A human-readable line for the tray, including why we are paused.</summary>
    public string Describe()
    {
        var reason = _power.Current;
        var state = reason == PauseReason.None ? "drawing" : $"paused ({reason})";
        return $"Verso — {state}\n{Status}";
    }

    public void Dispose()
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        if (_shellWatcher is not null)
        {
            _shellWatcher.ShellRestarted -= OnShellRestarted;
            _shellWatcher.Dispose();
        }

        _reassert.Stop();
        _power.Changed -= OnPauseChanged;
        _power.Dispose();

        foreach (var window in _panelWindows.Values)
        {
            window.Dispose();
        }
        _panelWindows.Clear();

        foreach (var window in _backgroundWindows.Values)
        {
            window.Dispose();
        }
        _backgroundWindows.Clear();
    }
}

/// <summary>The displays to put a desk on.</summary>
internal static class Displays
{
    /// <summary>
    /// Real per-monitor bounds in physical pixels, from
    /// <see cref="Monitors.All"/>. Falls back to one rectangle spanning the
    /// whole virtual screen only if that enumeration ever comes back empty —
    /// not expected, but a wallpaper drawn across the wrong bounds is a far
    /// better failure than no wallpaper at all.
    /// </summary>
    public static IEnumerable<Rectangle> All()
    {
        var monitors = Monitors.All();
        if (monitors.Count > 0)
        {
            return monitors;
        }

        return
        [
            new Rectangle(
                (int)SystemParameters.VirtualScreenLeft,
                (int)SystemParameters.VirtualScreenTop,
                (int)SystemParameters.VirtualScreenWidth,
                (int)SystemParameters.VirtualScreenHeight)
        ];
    }
}
