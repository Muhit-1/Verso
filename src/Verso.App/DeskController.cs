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
    private InputMirror? _inputMirror;
    private IReadOnlyList<IInputTarget> _inputTargets = [];
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
        _bridge.OutpostWidgetsChanged += NotifyWidgetsChanged;
        _bridge.OutpostThemeChanged += NotifyThemeChanged;
        _bridge.AppsChanged += NotifyAppsChanged;
        _bridge.NotesChanged += OnNotesChanged;
        _bridge.EditorClosed += OnEditorClosed;

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
        Hosting.StartupLog.Mark("desk: DetectLayout begins");
        _layout = DesktopLayer.DetectLayout();
        Hosting.StartupLog.Mark("desk: DetectLayout done; reconcile begins");

        await ReconcileAsync();
        Hosting.StartupLog.Mark("desk: reconcile done");

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
        _backgroundNote = null;

        // The Outpost desk: one window per monitor inside the Progman layer, holding the wallpaper AND
        // every widget, with the mouse mirrored in. It needs the layer (ProgmanLayer.PrepareAsync asks
        // Explorer for it); if the layer is not there we say so and fall back to the classic desk
        // rather than show nothing.
        var wantsOutpost = _settings.Current.DeskStyle == "outpost" && mode == DeskMode.Overlay;
        var useOutpost = false;
        if (wantsOutpost)
        {
            useOutpost = await ProgmanLayer.PrepareAsync();
            if (!useOutpost)
            {
                _backgroundNote = "Outpost desk unavailable (desktop layer not ready) - using the classic desk";
            }
        }

        // Classic panel windows exist only when the Outpost desk is not in use.
        var panelMonitors = useOutpost ? [] : current;

        foreach (var gone in _panelWindows.Keys.Where(k => !panelMonitors.Contains(k)).ToList())
        {
            _panelWindows[gone].Dispose();
            _panelWindows.Remove(gone);
        }

        foreach (var added in panelMonitors.Except(_panelWindows.Keys))
        {
            var window = new DeskWindow(_bridge, _settings, mode, _layout, added);
            _panelWindows[added] = window;
            await window.InitialiseAsync();
            await ApplyCurrentPauseAsync(window);
        }

        // Windows in the Progman layer. Outpost: always, one per monitor. Classic: only when there is
        // a video/gif/interactive page for the web stage to show ("image" goes straight to the real
        // Windows wallpaper, "none" has nothing to draw), and only in overlay mode — wallpaper mode's
        // single panel window already renders the stage itself.
        var layerMode = useOutpost ? DeskMode.Outpost : DeskMode.Background;
        var desiredBackgroundMonitors = new HashSet<Rectangle>();
        if (useOutpost)
        {
            desiredBackgroundMonitors = current;
        }
        else if (mode == DeskMode.Overlay
                 && _settings.Current.Background.Kind is "video" or "gif" or "interactive")
        {
            // Without Progman's WorkerW child the layer is invisible, and a fresh Explorer never has
            // one. If it does not appear, show nothing: never fall back to a window that would cover
            // the icons.
            if (await ProgmanLayer.PrepareAsync())
            {
                desiredBackgroundMonitors = current;
            }
            else
            {
                _backgroundNote = "live wallpaper unavailable (desktop layer not ready)";
            }
        }

        // Gone monitors, a changed background or desk style, and windows whose Explorer was restarted
        // underneath them (a Progman child dies with Progman).
        foreach (var gone in _backgroundWindows
                     .Where(pair => !desiredBackgroundMonitors.Contains(pair.Key)
                                    || pair.Value.Mode != layerMode
                                    || !pair.Value.IsSurfaceAlive)
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
                var window = new DeskWindow(_bridge, _settings, layerMode, _layout, added);
                _backgroundWindows[added] = window;
                await window.InitialiseAsync();
                await ApplyCurrentPauseAsync(window);
            }
            catch (InvalidOperationException ex)
            {
                _backgroundWindows.Remove(added);
                _backgroundNote = $"{(useOutpost ? "Outpost desk" : "live wallpaper")} unavailable ({ex.Message})";
            }
        }

        UpdateInputMirror();

        var first = _panelWindows.Values.Concat(_backgroundWindows.Values).FirstOrDefault();
        Status = first is null
            ? "no displays"
            : $"{current.Count} display(s) · {first.PlacementDescription}"
                + (!useOutpost && _backgroundWindows.Count > 0 ? $" · {_backgroundWindows.Count} live wallpaper surface(s)" : "")
                + (useOutpost ? (_inputMirror?.IsListening == true ? " · mouse mirrored" : " · MOUSE NOT MIRRORED") : "")
                + (_backgroundNote is null ? "" : $" · {_backgroundNote}");
    }

    /// <summary>Starts the mouse mirror while any Outpost window exists, stops it when none does.</summary>
    private void UpdateInputMirror()
    {
        _inputTargets = _backgroundWindows.Values
            .Where(w => w.Mode == DeskMode.Outpost)
            .Cast<IInputTarget>()
            .ToList();

        if (_inputTargets.Count == 0)
        {
            _inputMirror?.Dispose();
            _inputMirror = null;
            return;
        }

        try
        {
            _inputMirror ??= new InputMirror(() => _inputTargets);
        }
        catch (InvalidOperationException ex)
        {
            _backgroundNote = $"mouse mirror unavailable ({ex.Message})";
        }
    }

    /// <summary>
    /// A window created while the desk is paused (a maximised app in front, a
    /// locked session, battery saver…) must start paused. Without this a
    /// wallpaper picked at that moment kept decoding video nobody could see
    /// until the next pause-state change — and left the widgets hidden while
    /// the wallpaper showed, because only the existing windows were paused.
    /// </summary>
    private async Task ApplyCurrentPauseAsync(DeskWindow window)
    {
        if (_power.Current != PauseReason.None)
        {
            await window.SetRenderingAsync(false);
        }
    }

    /// <summary>Asks every Outpost window to forget where its elements were moved to.</summary>
    public void ResetOutpostLayout()
    {
        foreach (var window in _backgroundWindows.Values.Where(w => w.Mode == DeskMode.Outpost))
        {
            window.ResetLayout();
        }
    }

    /// <summary>Pushes a widget on/off change to every Outpost window (one per monitor).</summary>
    public void NotifyWidgetsChanged()
    {
        foreach (var window in _backgroundWindows.Values.Where(w => w.Mode == DeskMode.Outpost))
        {
            window.NotifyWidgetsChanged();
        }
    }

    /// <summary>Pushes a palette change to every Outpost window.</summary>
    public void NotifyThemeChanged()
    {
        foreach (var window in _backgroundWindows.Values.Where(w => w.Mode == DeskMode.Outpost))
        {
            window.NotifyThemeChanged();
        }
    }

    /// <summary>Tells every Outpost page about a host event ("notesChanged", "editorClosed"...); widgets listen with ctx.on.</summary>
    public void NotifyEvent(string name)
    {
        foreach (var window in _backgroundWindows.Values.Where(w => w.Mode == DeskMode.Outpost))
        {
            window.NotifyEvent(name);
        }
    }

    /// <summary>Pushes an app / box change to every Outpost window.</summary>
    public void NotifyAppsChanged()
    {
        foreach (var window in _backgroundWindows.Values.Where(w => w.Mode == DeskMode.Outpost))
        {
            window.NotifyAppsChanged();
        }
    }

    private void OnNotesChanged() => NotifyEvent("notesChanged");

    private void OnEditorClosed() => NotifyEvent("editorClosed");

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

        // All windows at once. Pausing one at a time let the overlay's
        // TrySuspendAsync — which can take seconds — hold up the wallpaper
        // window behind it, so a maximised app in front left the video
        // decoding (measured: ~11% CPU and GPU video decode while "paused").
        // Hiding is what saves the power, and it is quick; the slow part
        // (suspending the renderer) must not queue behind another window's.
        var windows = _panelWindows.Values.Concat(_backgroundWindows.Values).ToList();
        await Task.WhenAll(windows.Select(window => SetRenderingSafeAsync(window, render)));
    }

    private static async Task SetRenderingSafeAsync(DeskWindow window, bool render)
    {
        try
        {
            await window.SetRenderingAsync(render);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException)
        {
            // A window that is mid-teardown (display change, Explorer restart)
            // has nothing left to pause; the others still must be.
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

        _bridge.OutpostWidgetsChanged -= NotifyWidgetsChanged;
        _bridge.OutpostThemeChanged -= NotifyThemeChanged;
        _bridge.AppsChanged -= NotifyAppsChanged;
        _bridge.NotesChanged -= OnNotesChanged;
        _bridge.EditorClosed -= OnEditorClosed;
        _inputMirror?.Dispose();
        _inputMirror = null;

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
