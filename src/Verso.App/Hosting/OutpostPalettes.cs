using System.Text.Json.Serialization;
using Verso.Core.Models;

namespace Verso.App.Hosting;

/// <summary>
/// The colour palettes of the Outpost desk. The colours themselves live in web/outpost/css/outpost.css; this
/// is the list the dashboard and the desk menu show, so both always offer the same choices.
/// </summary>
internal static class OutpostPalettes
{
    public const string Default = "sunset";

    public sealed record Entry(string Id, string Name, string From, string To);

    public static readonly IReadOnlyList<Entry> All =
    [
        new("sunset", "Sunset", "#3a2540", "#ff9e64"),
        new("night", "Night", "#0a1428", "#FFD700"),
        new("evening", "Evening", "#1e1040", "#c084fc"),
        new("forest-green", "Forest green", "#0c2a1e", "#66cc99"),
        new("black-sunset", "Black sunset", "#050505", "#ff7a00"),
        new("pencil", "Pencil", "#141414", "#bbbbbb")
    ];

    public static bool Contains(string id) => All.Any(e => e.Id == id);

    public sealed class Info
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("from")] public string From { get; set; } = "";
        [JsonPropertyName("to")] public string To { get; set; } = "";
        [JsonPropertyName("selected")] public bool Selected { get; set; }
    }

    /// <summary>The chosen palette id, or "" if the user has not chosen one yet.</summary>
    public static string Chosen(VersoSettings settings) => Contains(settings.OutpostTheme) ? settings.OutpostTheme : "";

    public static List<Info> Describe(VersoSettings settings)
    {
        var chosen = Chosen(settings);
        return All
            .Select(e => new Info { Id = e.Id, Name = e.Name, From = e.From, To = e.To, Selected = e.Id == chosen })
            .ToList();
    }
}
