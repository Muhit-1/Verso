using Verso.Core.Models;

namespace Verso.Core.Contracts;

/// <summary>
/// Whatever is currently playing on the machine. The Windows implementation
/// wraps <c>GlobalSystemMediaTransportControlsSessionManager</c>; nothing above
/// this interface knows that.
/// </summary>
public interface INowPlayingSource : IDisposable
{
    NowPlaying? Current { get; }
    IReadOnlyList<QueueItem> Queue { get; }

    event EventHandler<NowPlaying?>? Changed;

    Task PlayAsync();
    Task PauseAsync();
    Task NextAsync();
    Task PreviousAsync();

    /// <summary>Starts listening. Safe to call once.</summary>
    Task StartAsync(CancellationToken cancellationToken = default);
}
