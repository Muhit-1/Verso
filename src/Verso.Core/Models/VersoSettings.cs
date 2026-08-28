using System.Text.Json.Serialization;

namespace Verso.Core.Models;

/// <summary>
/// The whole of %APPDATA%\Verso\settings.json. Plain JSON on purpose —
/// a settings file is not a database.
/// </summary>
public sealed class VersoSettings
{
    /// <summary>Bumped only when a migration is needed.</summary>
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Id of the active desk theme folder, e.g. "smoked-petrol".</summary>
    [JsonPropertyName("themeId")]
    public string ThemeId { get; set; } = "smoked-petrol";

    /// <summary>
    /// Id of the active dashboard theme folder, e.g. "fieldnote". A separate
    /// choice from <see cref="ThemeId"/> — the dashboard and the desk are
    /// skinned independently. See docs/dashboard-theme-spec.md.
    /// </summary>
    [JsonPropertyName("dashboardThemeId")]
    public string DashboardThemeId { get; set; } = "fieldnote";

    [JsonPropertyName("startWithWindows")]
    public bool StartWithWindows { get; set; }

    [JsonPropertyName("power")]
    public PowerSettings Power { get; set; } = new();

    /// <summary>The wallpaper picked from the tray. See <see cref="BackgroundSettings"/>.</summary>
    [JsonPropertyName("background")]
    public BackgroundSettings Background { get; set; } = new();

    /// <summary>The "clean desktop" toggles from the tray. See <see cref="DesktopCleanupSettings"/>.</summary>
    [JsonPropertyName("desktopCleanup")]
    public DesktopCleanupSettings DesktopCleanup { get; set; } = new();

    /// <summary>Apps shown in the app-launcher widget. See <see cref="LauncherSettings"/>.</summary>
    [JsonPropertyName("launcher")]
    public LauncherSettings Launcher { get; set; } = new();

    /// <summary>Which widgets are turned off. See <see cref="WidgetSettings"/>.</summary>
    [JsonPropertyName("widgets")]
    public WidgetSettings Widgets { get; set; } = new();

    /// <summary>One entry per display, keyed by the device id Windows reports.</summary>
    [JsonPropertyName("displays")]
    public List<DisplaySettings> Displays { get; set; } = [];
}

/// <summary>
/// The rules that keep Verso off the CPU. Getting these wrong is the single
/// most common reason a live wallpaper gets uninstalled.
/// </summary>
public sealed class PowerSettings
{
    /// <summary>Stop rendering while any window is maximised or fullscreen.</summary>
    [JsonPropertyName("pauseOnFullscreen")]
    public bool PauseOnFullscreen { get; set; } = true;

    /// <summary>Stop rendering while the session is locked or the monitor is asleep.</summary>
    [JsonPropertyName("pauseOnLock")]
    public bool PauseOnLock { get; set; } = true;

    /// <summary>Stop rendering on battery below <see cref="BatteryThresholdPercent"/>.</summary>
    [JsonPropertyName("pauseOnBattery")]
    public bool PauseOnBattery { get; set; } = true;

    [JsonPropertyName("batteryThresholdPercent")]
    public int BatteryThresholdPercent { get; set; } = 30;

    /// <summary>Ceiling for the video layer. 30 is invisible on a wallpaper and halves the cost.</summary>
    [JsonPropertyName("maxFps")]
    public int MaxFps { get; set; } = 30;
}

/// <summary>
/// The "clean desktop" toggles from the tray: hide the taskbar, hide the
/// desktop icons. Independent of everything else Verso draws.
/// </summary>
public sealed class DesktopCleanupSettings
{
    [JsonPropertyName("hideTaskbar")]
    public bool HideTaskbar { get; set; }

    [JsonPropertyName("hideDesktopIcons")]
    public bool HideDesktopIcons { get; set; }
}

/// <summary>
/// Which widgets the user turned off — an allowlist of exclusions, not
/// inclusions, so a widget added later (a new bundled one, or one dropped
/// into the user widgets folder) defaults to on without needing an entry.
/// </summary>
public sealed class WidgetSettings
{
    [JsonPropertyName("disabled")]
    public List<string> Disabled { get; set; } = [];
}

/// <summary>What a single monitor is showing.</summary>
public sealed class DisplaySettings
{
    /// <summary>Stable device id from the display, not the index — indices shuffle on hot-plug.</summary>
    [JsonPropertyName("deviceId")]
    public string DeviceId { get; set; } = string.Empty;

    /// <summary>"web", "video", "image" or "none".</summary>
    [JsonPropertyName("wallpaperKind")]
    public string WallpaperKind { get; set; } = "web";

    /// <summary>Absolute path or URL of the wallpaper source.</summary>
    [JsonPropertyName("wallpaperSource")]
    public string WallpaperSource { get; set; } = string.Empty;

    /// <summary>Whether the widget desk is drawn on this display.</summary>
    [JsonPropertyName("showWidgets")]
    public bool ShowWidgets { get; set; } = true;
}
