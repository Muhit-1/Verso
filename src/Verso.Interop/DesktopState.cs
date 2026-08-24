using Windows.Win32;
using Windows.Win32.System.Power;
using Windows.Win32.UI.Shell;

namespace Verso.Interop;

/// <summary>
/// The machine conditions that decide whether Verso should be drawing.
/// </summary>
/// <remarks>
/// Read in one call rather than scattered across the app, so the pause rules
/// stay a single readable expression in <c>PowerGate</c>.
/// </remarks>
public readonly record struct DesktopState(
    bool FullscreenAppRunning,
    bool MonitorOff,
    bool PresentationMode,
    bool OnBattery,
    int BatteryPercent)
{
    /// <summary>
    /// Samples the current state. Cheap enough to poll every couple of seconds.
    /// </summary>
    public static DesktopState Query()
    {
        var fullscreen = false;
        var monitorOff = false;
        var presenting = false;

        // The same signal Windows uses to decide whether to show a notification.
        // Better than comparing window rects to the monitor: it already knows
        // about games, video players and presentations, and it does not fire for
        // a merely maximised window.
        if (PInvoke.SHQueryUserNotificationState(out var state).Succeeded)
        {
            fullscreen = state
                is QUERY_USER_NOTIFICATION_STATE.QUNS_BUSY
                or QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN;

            presenting = state == QUERY_USER_NOTIFICATION_STATE.QUNS_PRESENTATION_MODE;

            // Nothing on screen to draw for, and the cheapest win available.
            monitorOff = state == QUERY_USER_NOTIFICATION_STATE.QUNS_NOT_PRESENT;
        }

        var onBattery = false;
        var batteryPercent = -1;

        if (PInvoke.GetSystemPowerStatus(out var power))
        {
            // ACLineStatus: 0 offline, 1 online, 255 unknown.
            onBattery = power.ACLineStatus == 0;

            // BatteryLifePercent is 255 when unknown, which is what a desktop
            // with no battery reports. -1 means "do not apply the rule".
            batteryPercent = power.BatteryLifePercent == 255 ? -1 : power.BatteryLifePercent;
        }

        return new DesktopState(fullscreen, monitorOff, presenting, onBattery, batteryPercent);
    }
}
