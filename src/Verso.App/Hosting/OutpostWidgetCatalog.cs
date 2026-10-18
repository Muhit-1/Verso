using System.Text.Json.Serialization;
using Verso.Core.Models;

namespace Verso.App.Hosting;

/// <summary>
/// The widgets of the Outpost desk and whether each is on.
/// </summary>
/// <remarks>
/// <para>
/// The Independent Widget contract (CLAUDE.md, "ROUND 2"): every widget has its own entry here, its
/// own on/off flag in <see cref="VersoSettings.OutpostWidgets"/>, and — on the web side — its own module,
/// element, position and saved state. Nothing here couples one widget to another; switching one on or
/// off never touches the rest.
/// </para>
/// <para>
/// Unlike the classic iframe widgets (<see cref="WidgetCatalog"/>, an exclusion list so new widgets
/// default to on), Outpost widgets default per widget: a fresh desk shows only the clock and the
/// now-playing pill, plus the round menu button, which is not a widget and cannot be turned off.
/// </para>
/// </remarks>
internal static class OutpostWidgetCatalog
{
    public sealed record Entry(string Id, string Name, string Description, bool DefaultEnabled);

    public static readonly IReadOnlyList<Entry> All =
    [
        new("clock", "Clock", "The big clock and date. Click AM/PM on the desk to switch between 12 and 24 hour.", true),
        new("np", "Now playing", "What Windows is playing, with play, pause and skip. Click it on the desk to expand.", true),
        new("pomo", "Pomodoro", "A focus timer with work and break presets.", false),
        new("sw", "Stopwatch", "A stopwatch with laps.", false),
        new("notes", "Sticky notes", "Notes you can place anywhere, resize and colour. Click a note to type in it.", false),
        new("launcher", "App launcher", "See-through boxes of apps, placed anywhere on the desk — instead of taskbar pins.", false),
        new("flies", "Fireflies", "Slow drifting lights over the wallpaper. Uses a little extra power.", false)
    ];

    public static bool Contains(string id) => All.Any(e => e.Id == id);

    public static bool IsEnabled(VersoSettings settings, string id)
    {
        var entry = All.FirstOrDefault(e => e.Id == id);
        if (entry is null)
        {
            return false;
        }

        return settings.OutpostWidgets.TryGetValue(id, out var on) ? on : entry.DefaultEnabled;
    }

    /// <summary>The shape the web layer and the dashboard read: id, name, description, enabled.</summary>
    public sealed class Info
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("description")] public string Description { get; set; } = "";
        [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    }

    public static List<Info> Describe(VersoSettings settings) => All
        .Select(e => new Info { Id = e.Id, Name = e.Name, Description = e.Description, Enabled = IsEnabled(settings, e.Id) })
        .ToList();
}
