using System.Text.Json.Serialization;

namespace Verso.Core.Models;

/// <summary>
/// A widget.json, one per folder under web/widgets/ or %APPDATA%\Verso\widgets\.
/// A widget is a folder with an index.html and this file. It may reach the
/// outside world only through window.VersoHost — see docs/widget-spec.md.
/// </summary>
public sealed class WidgetManifest
{
    /// <summary>Must equal the folder name. Lowercase kebab-case.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public string Author { get; set; } = string.Empty;

    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    /// <summary>Entry point, relative to the widget folder.</summary>
    [JsonPropertyName("entry")]
    public string Entry { get; set; } = "index.html";

    /// <summary>
    /// Host capabilities the widget needs: "tasks", "media", "system", "network".
    /// The host grants exactly these and nothing else. A widget that asks for
    /// nothing gets a bridge with no methods on it.
    /// </summary>
    [JsonPropertyName("permissions")]
    public string[] Permissions { get; set; } = [];

    /// <summary>Preferred size in grid cells, used the first time it is placed.</summary>
    [JsonPropertyName("defaultSize")]
    public WidgetSize DefaultSize { get; set; } = new();

    /// <summary>Whether the widget needs mouse input forwarded to it (Phase 2).</summary>
    [JsonPropertyName("interactive")]
    public bool Interactive { get; set; }

    /// <summary>
    /// How often the widget expects to repaint, in milliseconds. The host uses
    /// this to throttle it when hidden. 0 means event-driven only.
    /// </summary>
    [JsonPropertyName("tickMs")]
    public int TickMs { get; set; }

    [JsonPropertyName("minShellVersion")]
    public string MinShellVersion { get; set; } = "0.1.0";
}

public sealed class WidgetSize
{
    [JsonPropertyName("w")]
    public int W { get; set; } = 4;

    [JsonPropertyName("h")]
    public int H { get; set; } = 3;
}
