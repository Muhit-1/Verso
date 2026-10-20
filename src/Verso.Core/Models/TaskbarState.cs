using System.Text.Json.Serialization;

namespace Verso.Core.Models;

/// <summary>
/// The Windows taskbar options Verso can read and write, as the Windows
/// Settings page shows them. Values follow what was measured on this Windows
/// build (see CLAUDE.md, "taskbar spike S0").
/// </summary>
public sealed class TaskbarState
{
    /// <summary>0 left, 1 top, 2 right, 3 bottom.</summary>
    [JsonPropertyName("position")] public int Position { get; set; } = 3;

    /// <summary>0 left, 1 centre.</summary>
    [JsonPropertyName("align")] public int Align { get; set; } = 1;

    /// <summary>Small taskbar (32 px). There is no "large" on this build.</summary>
    [JsonPropertyName("small")] public bool Small { get; set; }

    [JsonPropertyName("auto")] public bool Auto { get; set; }

    [JsonPropertyName("badges")] public bool Badges { get; set; } = true;

    [JsonPropertyName("flash")] public bool Flash { get; set; } = true;

    [JsonPropertyName("share")] public bool Share { get; set; } = true;

    [JsonPropertyName("corner")] public bool Corner { get; set; } = true;

    /// <summary>0 always combine, 1 when full, 2 never.</summary>
    [JsonPropertyName("combine")] public int Combine { get; set; }

    [JsonPropertyName("search")] public bool Search { get; set; } = true;

    [JsonPropertyName("taskview")] public bool TaskView { get; set; } = true;
}
