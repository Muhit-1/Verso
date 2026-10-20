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

    /// <summary>
    /// The boxes the user made on the Outpost desk. "Recent" is built in and never stored here — it is
    /// where a newly added app lands, and where the apps of a deleted box go.
    /// </summary>
    [JsonPropertyName("boxes")]
    public List<LauncherBox> Boxes { get; set; } = [];
}

/// <summary>One see-through box of apps on the Outpost desk.</summary>
public sealed class LauncherBox
{
    /// <summary>Id of the built-in box that holds new apps.</summary>
    public const string RecentId = "recent";

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
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

    /// <summary>
    /// Where a shortcut was copied from (a desktop icon, say), if Verso made its own copy. Only used to recognise the
    /// same icon being added twice.
    /// </summary>
    [JsonPropertyName("from")]
    public string From { get; set; } = string.Empty;

    /// <summary>The box this app sits in. Entries saved before boxes existed read as "recent".</summary>
    [JsonPropertyName("boxId")]
    public string BoxId { get; set; } = LauncherBox.RecentId;
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
