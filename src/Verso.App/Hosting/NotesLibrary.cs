using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Verso.App.Storage;

namespace Verso.App.Hosting;

/// <summary>
/// The sticky notes of the Outpost desk: text, colour, nothing else (position and size are the layout's business).
/// </summary>
/// <remarks>
/// Kept in their own file (outpost-notes.json), not in settings.json. A note is rich text, stored as a small safe
/// subset of HTML (<see cref="Sanitize"/>), so whatever reaches the desk page can never run script.
/// Like <see cref="LauncherLibrary"/>, one instance serves the desk and the dashboard; every change raises
/// <see cref="Changed"/>.
/// </remarks>
internal sealed partial class NotesLibrary(Storage.SettingsStore settings, FileLayoutStore store)
{
    public const string NotesWidgetId = "notes";

    /// <summary>The six note colours of the approved mockup, used in rotation for new notes.</summary>
    public static readonly string[] Colors = ["#7ac55c", "#5aa9e6", "#f2c94c", "#f08fb0", "#b48ae0", "#a8a8a8"];

    private const int MaxHtml = 20000;

    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly Lock _gate = new();

    public event Action? Changed;
    public event Action? WidgetsChanged;

    public sealed class Note
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("color")] public string Color { get; set; } = Colors[0];
        [JsonPropertyName("html")] public string Html { get; set; } = "";
    }

    public List<Note> Describe()
    {
        lock (_gate)
        {
            return Read();
        }
    }

    public Note? Get(string id) => Describe().Find(n => n.Id == id);

    /// <summary>Adds an empty note and switches the notes widget on so it shows at once.</summary>
    public string NewNote()
    {
        var id = "n" + Guid.NewGuid().ToString("N")[..8];

        lock (_gate)
        {
            var all = Read();
            all.Add(new Note { Id = id, Color = Colors[all.Count % Colors.Length] });
            Write(all);
        }

        settings.Update(s => s.OutpostWidgets[NotesWidgetId] = true);
        WidgetsChanged?.Invoke();
        Changed?.Invoke();
        return id;
    }

    public void SetHtml(string id, string html)
    {
        var clean = Sanitize(html);
        Edit(id, n => n.Html = clean);
    }

    public void SetColor(string id, string color)
    {
        if (Array.IndexOf(Colors, color) < 0)
        {
            return;
        }

        Edit(id, n => n.Color = color);
    }

    public void Delete(string id)
    {
        lock (_gate)
        {
            var all = Read();
            if (all.RemoveAll(n => n.Id == id) == 0)
            {
                return;
            }

            Write(all);
        }

        Changed?.Invoke();
    }

    private void Edit(string id, Action<Note> change)
    {
        lock (_gate)
        {
            var all = Read();
            var note = all.Find(n => n.Id == id);
            if (note is null)
            {
                return;
            }

            change(note);
            Write(all);
        }

        Changed?.Invoke();
    }

    private List<Note> Read()
    {
        try
        {
            var text = store.Load();
            if (!text.TrimStart().StartsWith('['))
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<Note>>(text, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private void Write(List<Note> all) => store.Save(JsonSerializer.Serialize(all, Json));

    // ---- sanitising ------------------------------------------------------------

    private static readonly HashSet<string> Allowed = ["b", "i", "u", "s", "strike", "ul", "ol", "li", "br", "div", "p"];

    [GeneratedRegex(@"<(script|style)\b.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Blocks();

    [GeneratedRegex(@"<(/?)([a-zA-Z][a-zA-Z0-9]*)[^>]*?(/?)>")]
    private static partial Regex Tags();

    /// <summary>
    /// Keeps only a few formatting tags and strips every attribute; anything else is dropped (its text stays).
    /// A stray angle bracket becomes plain text.
    /// </summary>
    public static string Sanitize(string? html)
    {
        if (string.IsNullOrEmpty(html))
        {
            return "";
        }

        if (html.Length > MaxHtml)
        {
            html = html[..MaxHtml];
        }

        html = Blocks().Replace(html, "");

        var result = new StringBuilder();
        var last = 0;
        foreach (Match m in Tags().Matches(html))
        {
            AppendText(result, html[last..m.Index]);
            var tag = m.Groups[2].Value.ToLowerInvariant();
            if (Allowed.Contains(tag))
            {
                result.Append(tag == "br" ? "<br>" : $"<{m.Groups[1].Value}{tag}>");
            }

            last = m.Index + m.Length;
        }

        AppendText(result, html[last..]);
        return result.ToString();
    }

    private static void AppendText(StringBuilder sb, string text) => sb.Append(text.Replace("<", "&lt;").Replace(">", "&gt;"));
}
