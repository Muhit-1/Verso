using System.Text.Json.Serialization;

namespace Verso.Core.Models;

/// <summary>
/// One time-blocked item in the agenda widget. Mirrors <c>{id, title, start, end}</c>
/// in JS. <see cref="Start"/> and <see cref="End"/> are same-day times as "HH:mm" —
/// the widget shows what's left of today, not a calendar across days.
/// </summary>
public sealed class AgendaItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("start")]
    public string Start { get; set; } = "00:00";

    [JsonPropertyName("end")]
    public string End { get; set; } = "00:00";
}
