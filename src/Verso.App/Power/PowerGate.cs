using Microsoft.Win32;
using System.Windows.Threading;
using Verso.Core.Contracts;
using Verso.Core.Models;
using Verso.Interop;

namespace Verso.App.Power;

/// <summary>
/// Decides when Verso is allowed to draw.
/// </summary>
/// <remarks>
/// <para>
/// Load-bearing, not a nicety. An unthrottled WebView2 running every waking
/// hour is the difference between an app people forget is installed and one
/// they uninstall in a week.
/// </para>
/// <para>
/// The plan put all of this in Phase 4. Two of the rules moved forward to
/// Phase 1 — fullscreen and lock — because you run this on your own machine
/// daily from Phase 1 onward, and months of unthrottled rendering is
/// self-inflicted.
/// </para>
/// <para>
/// Fullscreen detection uses <c>SHQueryUserNotificationState</c> rather than
/// comparing window rectangles to the monitor. It is the same signal Windows
/// itself uses to decide whether to suppress notifications, so it already
/// knows about games, presentations and video players, and it does not fire on
/// a merely maximised window.
/// </para>
/// </remarks>
internal sealed class PowerGate : IPowerGate
{
    private readonly DispatcherTimer _poll;
    private readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    private bool _disposed;
    private PowerSettings _settings;
    private PauseReason _current;

    public PowerGate(PowerSettings settings)
    {
        _settings = settings;

        // Two seconds. Fast enough that alt-tabbing into a game pauses us before
        // the loading screen finishes; slow enough to be free.
        _poll = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _poll.Tick += (_, _) => Evaluate();
    }

    public PauseReason Current => _current;

    public event EventHandler<PauseReason>? Changed;

    /// <summary>The PC woke from sleep or hibernation. Raised on the UI thread.</summary>
    public event Action? Resumed;

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
        _poll.Start();
        Evaluate();
        return Task.CompletedTask;
    }

    /// <summary>Applies changed settings and re-evaluates immediately.</summary>
    public void Update(PowerSettings settings)
    {
        _settings = settings;
        Evaluate();
    }

    /// <summary>Pauses or resumes on the user's explicit request, from the tray.</summary>
    public void SetUserPaused(bool paused)
    {
        _userPaused = paused;
        Evaluate();
    }

    private bool _userPaused;
    private bool _sessionLocked;

    // SystemEvents raises its events from its own thread. Evaluate() ends in window and WebView2 calls, which
    // belong to the UI thread, so both handlers hop over before touching anything.
    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        bool? locked = e.Reason switch
        {
            SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff => true,
            SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon => false,
            _ => null
        };

        _ui.InvokeAsync(() =>
        {
            if (_disposed)
            {
                return;
            }

            if (locked is { } value)
            {
                _sessionLocked = value;
            }

            Evaluate();
        });
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        var resumed = e.Mode == PowerModes.Resume;

        _ui.InvokeAsync(() =>
        {
            if (_disposed)
            {
                return;
            }

            Evaluate();
            if (resumed)
            {
                Resumed?.Invoke();
            }
        });
    }

    private void Evaluate()
    {
        var reason = PauseReason.None;

        if (_userPaused)
        {
            reason |= PauseReason.UserRequested;
        }

        if (_settings.PauseOnLock && _sessionLocked)
        {
            reason |= PauseReason.SessionLocked;
        }

        var state = DesktopState.Query();

        if (_settings.PauseOnFullscreen && state.FullscreenAppRunning)
        {
            reason |= PauseReason.Fullscreen;
        }

        if (state.MonitorOff)
        {
            reason |= PauseReason.MonitorOff;
        }

        // Unconditional, same as MonitorOff — there is no scenario where
        // rendering something with nothing of it on screen is a choice a
        // user would want to keep. See DesktopState.Query() for what this
        // heuristic does and does not catch.
        if (state.LikelyOccluded)
        {
            reason |= PauseReason.NotVisible;
        }

        if (_settings.PauseOnBattery
            && state.OnBattery
            && state.BatteryPercent >= 0
            && state.BatteryPercent < _settings.BatteryThresholdPercent)
        {
            reason |= PauseReason.LowBattery;
        }

        if (reason == _current)
        {
            return;
        }

        _current = reason;
        Changed?.Invoke(this, reason);
    }

    public void Dispose()
    {
        _disposed = true;
        _poll.Stop();
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }
}
