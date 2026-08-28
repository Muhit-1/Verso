using System.Text.Json.Serialization;

namespace Verso.Core.Models;

/// <summary>
/// The apps the user chose to show in the app-launcher widget, persisted
/// to settings.json.
/// </summary>
/// <remarks>
/// Deliberately user-curated rather than read from Windows' pinned-taskbar
/// folder. That folder's location and format are undocumented and have
/// moved across Windows versions before — this project already spent
/// Phase 0 relearning that lesson once, on the WorkerW technique. A picker
/// the user drives themselves, the same pattern <c>DesktopActions.PickBackground</c>
/// already uses, does not have that failure mode.
/// </remarks>
public sealed class LauncherSettings
{
    [JsonPropertyName("entries")]
    public List<LauncherEntry> Entries { get; set; } = [];
}

public sealed class LauncherEntry
{
    /// <summary>
    /// Generated once, when the entry is added — not derived from its
    /// position in the list, which can change. This is what the widget
    /// actually hands back to launch it; see <see cref="LauncherInfo"/>.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Shown in the widget. Defaults to the file name if not set explicitly.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Absolute path to an .exe or .lnk. Never sent to script — see <see cref="LauncherInfo"/>.</summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;
}

/// <summary>
/// One entry, resolved for the web layer: an opaque id it can hand back to
/// launch the app, never the filesystem path <see cref="LauncherEntry.Path"/>
/// holds. Same reasoning as <c>BackgroundInfo</c>.
/// </summary>
public sealed class LauncherInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
}
