using System.Text.Json.Serialization;

namespace Verso.Core.Models;

/// <summary>
/// What the user chose from the tray's "Set background…" picker, persisted to
/// settings.json. "image" is applied straight to the real Windows wallpaper —
/// see <c>DesktopWallpaper.SetImage</c> in Verso.Interop — and never needs the
/// web layer at all. "video" and "interactive" are rendered by the shell's
/// own stage, and only take effect where the stage has pixels to draw in.
/// See docs/plan.md, Phase 3.
/// </summary>
public sealed class BackgroundSettings
{
    /// <summary>"none", "image", "video" or "interactive".</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "none";

    /// <summary>Absolute path to the file the user picked. Never sent to script.</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;
}

/// <summary>
/// The background, resolved for the web layer: a URL it can actually load,
/// never the filesystem path <see cref="BackgroundSettings.Source"/> holds.
/// </summary>
public sealed class BackgroundInfo
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "none";

    /// <summary>Null unless <see cref="Kind"/> is "video" or "interactive".</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }
}
