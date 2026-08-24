namespace Verso.Core.Contracts;

/// <summary>Why rendering is currently suppressed.</summary>
[Flags]
public enum PauseReason
{
    None = 0,
    Fullscreen = 1 << 0,
    SessionLocked = 1 << 1,
    MonitorOff = 1 << 2,
    LowBattery = 1 << 3,
    NotVisible = 1 << 4,
    UserRequested = 1 << 5
}

/// <summary>
/// Decides when Verso is allowed to draw.
/// </summary>
/// <remarks>
/// This is load-bearing, not a nicety. An unthrottled WebView2 plus a video
/// surface running every waking hour is the difference between an app people
/// forget is installed and one they uninstall in a week.
/// </remarks>
public interface IPowerGate : IDisposable
{
    /// <summary>Nothing should be drawing while this is anything but <see cref="PauseReason.None"/>.</summary>
    PauseReason Current { get; }

    bool ShouldRender => Current == PauseReason.None;

    event EventHandler<PauseReason>? Changed;

    Task StartAsync(CancellationToken cancellationToken = default);
}
