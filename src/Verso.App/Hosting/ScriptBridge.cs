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

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly ITaskStore _tasks;
    private readonly ThemeCatalog _themes;
    private readonly SettingsStore _settings;

    internal ScriptBridge(ITaskStore tasks, ThemeCatalog themes, SettingsStore settings)
    {
        _tasks = tasks;
        _themes = themes;
        _settings = settings;
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

    // ---- media ----------------------------------------------------------

    /// <returns>
    /// JSON <c>NowPlaying</c>, or <c>"null"</c> when nothing is playing.
    /// </returns>
    /// <remarks>
    /// Phase 3 fills this from <c>GlobalSystemMediaTransportControlsSessionManager</c>.
    /// Until then it returns null on purpose: the web layer treats a null answer
    /// as "no host media" and falls back to its demo player, which is exactly the
    /// behaviour we want while the rest of the app is being built.
    /// </remarks>
    public string GetNowPlaying() => "null";

    /// <returns>JSON array of <c>{title, artist}</c>. Empty until Phase 3.</returns>
    public string GetQueue() => "[]";

    public void Play() { }
    public void Pause() { }
    public void Next() { }
    public void Previous() { }

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

    public void Dispose()
    {
        // Nothing owned yet. The media session subscription lands here in Phase 3.
    }
}
