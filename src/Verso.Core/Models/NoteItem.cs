using System.Text.Json.Serialization;

namespace Verso.Core.Models;

/// <summary>One entry in the notes widget. Mirrors <c>{id, text}</c> in JS.</summary>
public sealed class NoteItem
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}
