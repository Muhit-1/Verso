using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Verso.App.Hosting;

/// <summary>
/// The object that appears in the picker window's page as
/// <c>window.chrome.webview.hostObjects.VersoPicker</c>. Only that window gets it.
/// </summary>
/// <remarks>
/// Hands the page the installed-apps list and each app's icon, adds the apps the user ticked to a box, and can fall back
/// to the Windows file picker. Targets (file paths or <c>shell:AppsFolder\...</c> addresses) are only ever echoed back
/// from the list the host itself produced — a page cannot make the launcher run anything the list did not contain.
/// </remarks>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.AutoDual)]
public sealed class PickerBridge
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly LauncherLibrary _library;
    private readonly Action _close;
    private readonly Action<string> _browse;
    private Task<List<InstalledApps.Entry>> _loading = Task.FromResult(new List<InstalledApps.Entry>());

    internal PickerBridge(LauncherLibrary library, Action close, Action<string> browse)
    {
        _library = library;
        _close = close;
        _browse = browse;
    }

    /// <summary>Starts (or reuses) the enumeration of installed apps; the page polls <see cref="GetApps"/> for the result.</summary>
    internal void Reload() => _loading = InstalledApps.GetAsync();

    private sealed class Row
    {
        [JsonPropertyName("target")] public string Target { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("source")] public string Source { get; set; } = "";
        [JsonPropertyName("added")] public bool Added { get; set; }
    }

    /// <returns>JSON array of <c>{id, name}</c>, Recent first — the boxes an app can be added to.</returns>
    public string GetBoxes() => JsonSerializer.Serialize(
        _library.Describe().Select(b => new { id = b.Id, name = b.Name }), Json);

    /// <returns>
    /// JSON array of <c>{target, name, source, added}</c>, or <c>""</c> while the list is still being read (the page asks
    /// again a moment later).
    /// </returns>
    public string GetApps()
    {
        if (!_loading.IsCompleted)
        {
            return "";
        }

        var taken = _library.Targets();
        var rows = _loading.Result.Select(e => new Row
        {
            Target = e.Target,
            Name = e.Name,
            Source = e.Source,
            Added = taken.Contains(e.Target)
        });

        return JsonSerializer.Serialize(rows, Json);
    }

    /// <returns>A <c>data:image/png</c> URL, or <c>""</c>.</returns>
    public string GetIcon(string target) => _loading.IsCompleted && _loading.Result.Any(e => e.Target == target)
        ? ShellIcons.GetDataUrl(target)
        : "";

    /// <param name="json">JSON array of <c>{target, name}</c> — only targets from <see cref="GetApps"/> are accepted.</param>
    public void Add(string json, string boxId)
    {
        if (!_loading.IsCompleted)
        {
            return;
        }

        var known = _loading.Result.ToDictionary(e => e.Target, e => e.Name);
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var target = item.GetProperty("target").GetString() ?? "";
                if (known.TryGetValue(target, out var name))
                {
                    _library.AddApp(target, boxId, raise: false, name: name);
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            // malformed request from the page: add nothing more
        }

        _library.NotifyChanged();
    }

    /// <summary>Falls back to the Windows file picker (for a program the list does not show).</summary>
    public void Browse(string boxId) => _browse(boxId);

    public void Close() => _close();
}
