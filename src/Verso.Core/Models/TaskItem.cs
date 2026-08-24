using System.Text.Json.Serialization;

namespace Verso.Core.Models;

/// <summary>One line in the tasks widget. Mirrors <c>{id, text, done}</c> in JS.</summary>
public sealed class TaskItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("done")]
    public bool Done { get; set; }
}
