using System.Drawing;
using System.Windows;
using System.Windows.Threading;
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
/// One window per display. Multi-monitor hot-plug and per-display configuration
/// are Phase 4; this creates a window for each screen that exists at startup,
/// which is enough to be correct on a fixed setup and honest about the rest.
/// </remarks>
internal sealed class DeskController : IDisposable
{
    private readonly ScriptBridge _bridge;
    private readonly SettingsStore _settings;
    private readonly PowerGate _power;
    private readonly List<DeskWindow> _windows = [];
    private readonly DispatcherTimer _reassert;

    private DesktopLayer.DesktopLayout _layout;

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
        _reassert.Tick += (_, _) => _windows.ForEach(w => w.ReassertPlacement());
    }

    /// <summary>What happened when the desk was placed. Shown in the tray tooltip.</summary>
    public string Status { get; private set; } = "starting";

    public bool IsShown { get; private set; }

    public async Task StartAsync()
    {
        // Done once, at startup: it sends a message to Explorer, and the answer
        // does not change while the session is running.
        _layout = DesktopLayer.DetectLayout();

        var mode = _layout == DesktopLayer.DesktopLayout.Splittable
            ? DeskMode.Wallpaper
            : DeskMode.Overlay;

        foreach (var screen in Displays.All())
        {
            var window = new DeskWindow(_bridge, _settings, mode, _layout, screen);
            _windows.Add(window);
            await window.InitialiseAsync();
        }

        Status = _windows.Count == 0
            ? "no displays"
            : $"{_windows.Count} display(s) · {_windows[0].PlacementDescription}";

        IsShown = true;

        _reassert.Start();
        await _power.StartAsync();
    }

    private async void OnPauseChanged(object? sender, PauseReason reason)
    {
        var render = reason == PauseReason.None;
        foreach (var window in _windows)
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

    public void ReloadWeb() => _windows.ForEach(w => w.ReloadWeb());

    /// <summary>A human-readable line for the tray, including why we are paused.</summary>
    public string Describe()
    {
        var reason = _power.Current;
        var state = reason == PauseReason.None ? "drawing" : $"paused ({reason})";
        return $"Verso — {state}\n{Status}";
    }

    public void Dispose()
    {
        _reassert.Stop();
        _power.Changed -= OnPauseChanged;
        _power.Dispose();

        foreach (var window in _windows)
        {
            window.Dispose();
        }

        _windows.Clear();
    }
}

/// <summary>The displays to put a desk on.</summary>
/// <remarks>
/// WPF has no screen enumeration of its own, and pulling in WinForms just for
/// <c>Screen.AllScreens</c> would drag a second UI framework into the process.
/// <c>SystemParameters</c> covers the single-display case correctly, which is
/// what Phase 1 needs; real per-monitor enumeration with DPI and hot-plug is
/// Phase 4.
/// </remarks>
internal static class Displays
{
    /// <summary>
    /// Bounds in physical pixels, which is what <see cref="DesktopSurface"/>
    /// wants. The app manifest declares PerMonitorV2 DPI awareness, so WPF's
    /// virtual-screen metrics are already unscaled device pixels on the primary
    /// display — true per-monitor enumeration with mixed DPI and hot-plug is
    /// Phase 4.
    /// </summary>
    public static IEnumerable<Rectangle> All()
    {
        yield return new Rectangle(
            (int)SystemParameters.VirtualScreenLeft,
            (int)SystemParameters.VirtualScreenTop,
            (int)SystemParameters.VirtualScreenWidth,
            (int)SystemParameters.VirtualScreenHeight);
    }
}
