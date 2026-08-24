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

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        _sessionLocked = e.Reason switch
        {
            SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff => true,
            SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon => false,
            _ => _sessionLocked
        };

        Evaluate();
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e) => Evaluate();

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
        _poll.Stop();
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }
}
