using System.Windows.Threading;
using Verso.App.Hosting;
using Verso.App.Storage;
using Verso.Core.Models;
using Verso.Interop;

namespace Verso.App.Dock;

/// <summary>
/// Owns the compact dock: while it is on, Windows' taskbar stays hidden and a small dock shows only the open apps.
/// </summary>
/// <remarks>
/// Everything is polled on two cheap timers while the dock is on (window list every 500 ms, pointer every 120 ms for the edge peek)
/// and both stop when it is off. Polling also makes it self-healing: an Explorer restart brings the taskbar back and the next tick hides it again.
/// </remarks>
internal sealed class DockController : IDisposable
{
    private readonly SettingsStore _settings;
    private readonly DockWindowList _list = new();
    private readonly DispatcherTimer _tick;
    private readonly DispatcherTimer _pointer;
    private CompactDockWindow? _window;
    private bool _peeking;
    private int _edgeTicks;
    private int _awayTicks;
    private int _position = 3;   // taskbar edge, refreshed by the slow tick: the pointer timer must not touch the registry

    public DockController(SettingsStore settings)
    {
        _settings = settings;
        _tick = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        _tick.Tick += (_, _) => Update();
        _pointer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(120) };
        _pointer.Tick += (_, _) => WatchEdge();
    }

    public bool Enabled => _settings.Current.Dock.Enabled;

    /// <summary>Called at startup and whenever a dock setting changes.</summary>
    public void Apply()
    {
        var dock = _settings.Current.Dock;

        if (!dock.Enabled)
        {
            _tick.Stop();
            _pointer.Stop();
            _peeking = false;
            _window?.Close();
            _window = null;
            DesktopChrome.SetTaskbarVisible(!_settings.Current.DesktopCleanup.HideTaskbar);
            return;
        }

        if (_window is null)
        {
            _window = new CompactDockWindow(_list);
            _window.EntryClicked += Activate;
            _window.StartClicked += PressStart;
            _ = new System.Windows.Interop.WindowInteropHelper(_window).EnsureHandle();   // a handle now, shown only when there is something to show
        }

        DesktopChrome.SetTaskbarVisible(false);
        _tick.Start();

        if (dock.Peek)
        {
            _pointer.Start();
        }
        else
        {
            _pointer.Stop();
            _peeking = false;
        }

        Update();
    }

    private void Update()
    {
        var dock = _settings.Current.Dock;
        if (!dock.Enabled || _window is null)
        {
            return;
        }

        // Windows' taskbar stays hidden (it comes back by itself after an Explorer restart); not while peeking.
        if (!_peeking && IsTaskbarVisible())
        {
            DesktopChrome.SetTaskbarVisible(false);
        }

        var entries = _list.Snapshot(dock.ShowMinimised);
        var foreground = NativeDock.GetForegroundWindow();
        var layout = TaskbarSettings.ReadLayout();
        _position = layout.Position;
        _window.Refresh(entries, foreground, dock, layout);

        var show = (entries.Count > 0 || dock.When == "always") && !FullScreenInFront(foreground);
        _window.SetShown(show);
    }

    /// <summary>A full-screen app (video, game, F11) hides the dock, like it hides the taskbar.</summary>
    private bool FullScreenInFront(IntPtr foreground)
    {
        if (foreground == IntPtr.Zero || foreground == _window?.Handle)
        {
            return false;
        }

        var cls = NativeDock.ClassOf(foreground);
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd")
        {
            return false;
        }

        if (!NativeDock.GetWindowRect(foreground, out var r))
        {
            return false;
        }

        var covers = r.Left <= 0 && r.Top <= 0
            && r.Right >= NativeDock.GetSystemMetrics(NativeDock.SM_CXSCREEN)
            && r.Bottom >= NativeDock.GetSystemMetrics(NativeDock.SM_CYSCREEN);

        // A maximised normal window also covers the screen while the taskbar is hidden, but it has a title bar.
        var style = (long)NativeDock.GetWindowLongPtr(foreground, NativeDock.GWL_STYLE);
        return covers && (style & NativeDock.WS_CAPTION) != NativeDock.WS_CAPTION;
    }

    private static bool IsTaskbarVisible()
    {
        var tray = NativeDock.FindWindow("Shell_TrayWnd", null);
        return tray != IntPtr.Zero && NativeDock.IsWindowVisible(tray);
    }

    // ---- edge peek --------------------------------------------------------------

    /// <summary>
    /// Pushing the pointer against the taskbar's screen edge slides the real taskbar in (clock, tray, notifications);
    /// moving away puts it back. Needs ~0.35 s at the edge so a quick flick does not trigger it.
    /// </summary>
    private void WatchEdge()
    {
        if (!Enabled || !NativeDock.GetCursorPos(out var p))
        {
            return;
        }

        var position = _position;
        var w = NativeDock.GetSystemMetrics(NativeDock.SM_CXSCREEN);
        var h = NativeDock.GetSystemMetrics(NativeDock.SM_CYSCREEN);

        var distance = position switch
        {
            1 => p.Y,
            0 => p.X,
            2 => w - 1 - p.X,
            _ => h - 1 - p.Y
        };
        var inside = p.X >= 0 && p.Y >= 0 && p.X < w && p.Y < h;

        if (inside && distance <= 1)
        {
            _awayTicks = 0;
            if (++_edgeTicks >= 3 && !_peeking)
            {
                _peeking = true;
                DesktopChrome.SetTaskbarVisible(true);
            }

            return;
        }

        _edgeTicks = 0;

        if (_peeking)
        {
            // 48 px taskbar + some slack; wait ~0.7 s so a slight overshoot does not close it under the pointer
            if (!inside || distance > 80)
            {
                if (++_awayTicks >= 6)
                {
                    _peeking = false;
                    _awayTicks = 0;
                    DesktopChrome.SetTaskbarVisible(false);
                }
            }
            else
            {
                _awayTicks = 0;
            }
        }
    }

    // ---- clicks -----------------------------------------------------------------

    private static void PressStart()
    {
        // The Windows key, so whatever Start menu this Windows build has opens.
        NativeDock.keybd_event(0x5B, 0, 0, UIntPtr.Zero);
        NativeDock.keybd_event(0x5B, 0, 2, UIntPtr.Zero);
    }

    private static void Activate(DockEntry entry)
    {
        var foreground = NativeDock.GetForegroundWindow();
        var windows = entry.Windows;

        IntPtr target;
        if (windows.Count == 1)
        {
            target = windows[0];
            if (!NativeDock.IsIconic(target) && target == foreground)
            {
                NativeDock.ShowWindow(target, NativeDock.SW_MINIMIZE);   // clicking the app you are in minimises it, like the taskbar
                return;
            }
        }
        else
        {
            var index = windows.IndexOf(foreground);
            target = index < 0 ? windows[0] : windows[(index + 1) % windows.Count];   // several windows: cycle through them
        }

        if (NativeDock.IsIconic(target))
        {
            NativeDock.ShowWindow(target, NativeDock.SW_RESTORE);
        }

        NativeDock.Foreground(target);
    }

    public void Dispose()
    {
        _tick.Stop();
        _pointer.Stop();
        _window?.Close();
        _window = null;
    }
}
