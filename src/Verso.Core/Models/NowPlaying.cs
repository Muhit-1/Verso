using System.Text.Json.Serialization;

namespace Verso.Core.Models;

/// <summary>
/// A snapshot of whatever is playing. On Windows this is filled from
/// <c>GlobalSystemMediaTransportControlsSessionManager</c>, so it works for
/// Spotify, YouTube Music, a browser tab — anything that registers with SMTC.
/// </summary>
public sealed class NowPlaying
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("artist")]
    public string Artist { get; set; } = string.Empty;

    [JsonPropertyName("album")]
    public string Album { get; set; } = string.Empty;

    [JsonPropertyName("durationMs")]
    public long DurationMs { get; set; }

    [JsonPropertyName("positionMs")]
    public long PositionMs { get; set; }

    [JsonPropertyName("isPlaying")]
    public bool IsPlaying { get; set; }

    /// <summary>
    /// Two hex colours sampled from the album art, dark first. The widget paints
    /// a gradient from them rather than showing the bitmap — cheaper, and it
    /// survives the art being unavailable.
    /// </summary>
    [JsonPropertyName("art")]
    public string[] Art { get; set; } = ["#2E5F63", "#7FB7A8"];
}

/// <summary>One upcoming track. Deliberately thinner than <see cref="NowPlaying"/>.</summary>
public sealed class QueueItem
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("artist")]
    public string Artist { get; set; } = string.Empty;
}
