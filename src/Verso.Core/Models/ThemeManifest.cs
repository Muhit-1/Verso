using System.Text.Json.Serialization;

namespace Verso.Core.Models;

/// <summary>
/// A theme.json, one per folder under web/themes/ or %APPDATA%\Verso\themes\.
/// A theme is only tokens: colours, type and a backdrop. It never contains
/// markup and never knows a widget's name — see docs/theme-spec.md.
/// </summary>
public sealed class ThemeManifest
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

    /// <summary>"dark" or "light". Only used to pick a sensible scrollbar and caret.</summary>
    [JsonPropertyName("scheme")]
    public string Scheme { get; set; } = "dark";

    /// <summary>Token stylesheet, relative to the theme folder.</summary>
    [JsonPropertyName("stylesheet")]
    public string Stylesheet { get; set; } = "theme.css";

    /// <summary>Optional swatch shown in the theme picker, relative to the theme folder.</summary>
    [JsonPropertyName("preview")]
    public string? Preview { get; set; }

    /// <summary>
    /// Stylesheet URLs to inject before the theme (Google Fonts, typically).
    /// Empty for offline-only themes; every theme must still declare local
    /// fallbacks in its font stacks.
    /// </summary>
    [JsonPropertyName("fonts")]
    public string[] Fonts { get; set; } = [];

    /// <summary>Lowest shell version this theme's token set is valid for.</summary>
    [JsonPropertyName("minShellVersion")]
    public string MinShellVersion { get; set; } = "0.1.0";
}
