using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using Verso.App.Storage;
using Verso.Core.Contracts;
using Verso.Core.Models;

namespace Verso.App.Hosting;

/// <summary>
/// The object that appears in JavaScript as <c>window.chrome.webview.hostObjects.VersoHost</c>.
/// </summary>
/// <remarks>
/// <para>
/// Every method takes and returns a JSON <see cref="string"/> rather than a typed
/// object. That looks clumsy next to <see cref="IVersoHost"/>, and it is
/// deliberate: WebView2 marshals host objects through COM, and COM does not
/// carry .NET collections or nullable value types across to JS in any way worth
/// debugging at 1am. One string, one <c>JSON.parse</c>, no marshalling surprises.
/// </para>
/// <para>
/// <see cref="IVersoHost"/> stays the typed contract that documents the shape and
/// that a future non-Windows host implements. This class is only the adapter.
/// </para>
/// <para>
/// The JS side never touches this directly either — <c>web/shell/js/host.js</c>
/// wraps it, caches the results and presents the small synchronous API the
/// widgets actually use.
/// </para>
/// </remarks>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.AutoDual)]
public sealed class ScriptBridge : IDisposable
{
    /// <summary>Bumped when the bridge shape changes in a way widgets can notice.</summary>
    public const string ContractVersion = "0.1.0";

    /// <summary>
    /// Virtual host the picked video/interactive background is mapped under
    /// by <see cref="Verso.App.Windows.DeskWindow"/>. Kept here, as a single
    /// constant, so the URL built in <see cref="GetBackground"/> and the
    /// mapping set up there can never drift apart.
    /// </summary>
    public const string BackgroundVirtualHost = "verso.bg.local";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ITaskStore _tasks;
    private readonly INoteStore _notes;
    private readonly IAgendaStore _agenda;
    private readonly ThemeCatalog _themes;
    private readonly WidgetCatalog _widgets;
    private readonly SettingsStore _settings;
    private readonly FileLayoutStore _layout;
    private readonly FileLayoutStore _deskState;
    private readonly INowPlayingSource _media;

    internal ScriptBridge(
        ITaskStore tasks,
        INoteStore notes,
        IAgendaStore agenda,
        ThemeCatalog themes,
        WidgetCatalog widgets,
        SettingsStore settings,
        FileLayoutStore layout,
        FileLayoutStore deskState,
        INowPlayingSource media)
    {
        _tasks = tasks;
        _notes = notes;
        _agenda = agenda;
        _themes = themes;
        _widgets = widgets;
        _settings = settings;
        _layout = layout;
        _deskState = deskState;
        _media = media;
    }

    // ---- identity -------------------------------------------------------

    public string HostVersion() => ContractVersion;

    // ---- tasks ----------------------------------------------------------

    /// <returns>JSON array of <c>{id, text, done}</c>.</returns>
    public string LoadTasks() => JsonSerializer.Serialize(_tasks.Load(), Json);

    /// <param name="json">JSON array of <c>{id, text, done}</c>.</param>
    public void SaveTasks(string json)
    {
        var parsed = JsonSerializer.Deserialize<List<TaskItem>>(json, Json);
        if (parsed is not null)
        {
            _tasks.Save(parsed);
        }
    }

    // ---- notes ------------------------------------------------------------

    /// <returns>JSON array of <c>{id, text}</c>.</returns>
    public string LoadNotes() => JsonSerializer.Serialize(_notes.Load(), Json);

    /// <param name="json">JSON array of <c>{id, text}</c>.</param>
    public void SaveNotes(string json)
    {
        var parsed = JsonSerializer.Deserialize<List<NoteItem>>(json, Json);
        if (parsed is not null)
        {
            _notes.Save(parsed);
        }
    }

    // ---- agenda -------------------------------------------------------------

    /// <returns>JSON array of <c>{id, title, start, end}</c>.</returns>
    public string LoadAgenda() => JsonSerializer.Serialize(_agenda.Load(), Json);

    /// <param name="json">JSON array of <c>{id, title, start, end}</c>.</param>
    public void SaveAgenda(string json)
    {
        var parsed = JsonSerializer.Deserialize<List<AgendaItem>>(json, Json);
        if (parsed is not null)
        {
            _agenda.Save(parsed);
        }
    }

    // ---- media ----------------------------------------------------------

    /// <returns>
    /// JSON <c>NowPlaying</c>, or <c>"null"</c> when nothing is playing.
    /// </returns>
    /// <remarks>
    /// Backed by <c>GlobalSystemMediaTransportControlsSessionManager</c> — see
    /// <c>SmtcNowPlayingSource</c>. Null is still a real, expected answer, not
    /// a stub: it means Windows currently has no active media session, and
    /// the web layer correctly falls back to its demo player for exactly
    /// that reason.
    /// </remarks>
    public string GetNowPlaying() => JsonSerializer.Serialize(_media.Current, Json);

    /// <returns>JSON array of <c>{title, artist}</c>. Empty for most sources — see host-api.md.</returns>
    public string GetQueue() => JsonSerializer.Serialize(_media.Queue, Json);

    public void Play() => _ = _media.PlayAsync();
    public void Pause() => _ = _media.PauseAsync();
    public void Next() => _ = _media.NextAsync();
    public void Previous() => _ = _media.PreviousAsync();

    // ---- theming --------------------------------------------------------

    public string GetThemeId() => _settings.Current.ThemeId;

    public void SetThemeId(string themeId)
    {
        if (string.IsNullOrWhiteSpace(themeId))
        {
            return;
        }

        _settings.Update(s => s.ThemeId = themeId);
    }

    /// <returns>JSON array of theme manifests, bundled and user-installed.</returns>
    public string GetThemes() => JsonSerializer.Serialize(_themes.Scan(), Json);

    // ---- widgets --------------------------------------------------------

    /// <returns>
    /// JSON array of widget manifests, bundled and user-installed, each
    /// carrying whether the user has it turned on. The loader
    /// (web/shell/js/widgets.js) skips anything not enabled — it never
    /// even creates an iframe for it, let alone touches the host.
    /// </returns>
    public string GetWidgets()
    {
        var manifests = _widgets.Scan();
        WidgetCatalog.ApplyEnabled(manifests, _settings.Current.Widgets.Disabled);
        return JsonSerializer.Serialize(manifests, Json);
    }

    // ---- launcher (permission: "apps") -------------------------------------

    /// <returns>JSON array of <c>{ id, name }</c>. Never the filesystem path.</returns>
    public string GetLauncherEntries()
    {
        var info = _settings.Current.Launcher.Entries
            .Select(e => new LauncherInfo { Id = e.Id, Name = e.Name })
            .ToList();

        return JsonSerializer.Serialize(info, Json);
    }

    /// <param name="id">An id from <see cref="GetLauncherEntries"/>. Anything else is silently ignored.</param>
    public void LaunchApp(string id)
    {
        var entry = _settings.Current.Launcher.Entries.Find(e => e.Id == id);
        if (entry is null || string.IsNullOrWhiteSpace(entry.Path))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(entry.Path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            // The app was uninstalled or moved since it was added. A widget
            // button that silently does nothing is a far better failure
            // than one that throws into a COM call.
        }
    }

    // ---- background -------------------------------------------------------

    /// <returns>
    /// JSON <c>{ kind, url }</c>. <c>url</c> is a
    /// <c>https://verso.bg.local/...</c> address the web layer can load
    /// directly — never the filesystem path, which script has no business
    /// seeing. There is no <c>SetBackground</c>: the choice is made from the
    /// tray's file picker, native-side, not from script.
    /// </returns>
    public string GetBackground()
    {
        var background = _settings.Current.Background;
        string? url = null;

        // "image" is included for the Outpost desk, which draws the picture itself inside
        // the wallpaper window; the classic desk ignores it (the image is the real Windows wallpaper there).
        if (background.Kind is "video" or "gif" or "interactive" or "image"
            && !string.IsNullOrWhiteSpace(background.Source)
            && File.Exists(background.Source))
        {
            url = $"https://{BackgroundVirtualHost}/{Uri.EscapeDataString(Path.GetFileName(background.Source))}";
        }

        return JsonSerializer.Serialize(new { kind = background.Kind, url }, Json);
    }

    // ---- layout ------------------------------------------------------------

    /// <returns>The stored layout JSON, or <c>"{}"</c>. Opaque — see <see cref="FileLayoutStore"/>.</returns>
    public string GetLayout() => _layout.Load();

    /// <param name="json">Opaque layout JSON. Stored verbatim, never parsed.</param>
    public void SaveLayout(string json) => _layout.Save(json);

    // ---- Outpost desk state -------------------------------------------------

    /// <returns>The Outpost desk's saved state (positions, palette, 12/24h), or <c>"{}"</c>. Opaque.</returns>
    public string GetDeskState() => _deskState.Load();

    /// <param name="json">Opaque JSON, stored verbatim.</param>
    public void SaveDeskState(string json) => _deskState.Save(json);

    public void Dispose()
    {
        // Nothing owned yet. The media session subscription lands here in Phase 3.
    }
}
