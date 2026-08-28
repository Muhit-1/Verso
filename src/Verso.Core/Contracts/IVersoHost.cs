using Verso.Core.Models;

namespace Verso.Core.Contracts;

/// <summary>
/// The one door between a widget and the machine.
/// </summary>
/// <remarks>
/// <para>
/// This is the C# side of <c>window.VersoHost</c>. The implementation is handed
/// to WebView2 with <c>CoreWebView2.AddHostObjectToScript("VersoHost", obj)</c>,
/// which requires the concrete class to be <c>[ComVisible(true)]</c> and marked
/// <c>[ClassInterface(ClassInterfaceType.AutoDual)]</c>.
/// </para>
/// <para>
/// The contract is deliberately small and deliberately JSON-shaped: every method
/// here has an exact counterpart in <c>web/shell/js/host.js</c>, and the demo
/// implementation in <c>demo-host.js</c> satisfies the same shape. That is what
/// lets the entire web layer be developed in a plain browser with no host at all.
/// </para>
/// <para>
/// Two rules that must not be relaxed. A widget never calls into .NET except
/// through this interface, and this interface never grows a method that hands
/// back a raw handle, path or process. Everything crossing the boundary is data.
/// </para>
/// </remarks>
public interface IVersoHost
{
    /// <summary>Shell contract version, so a widget can refuse to run on an old host.</summary>
    string HostVersion { get; }

    // ---- tasks (permission: "tasks") ----

    /// <summary>Reads the task list. Returns an empty list rather than throwing.</summary>
    IReadOnlyList<TaskItem> LoadTasks();

    /// <summary>Writes the task list. Persisted atomically to tasks.json.</summary>
    void SaveTasks(IReadOnlyList<TaskItem> tasks);

    // ---- media (permission: "media") ----

    /// <summary>Current playback state, or null when nothing has a media session.</summary>
    NowPlaying? GetNowPlaying();

    /// <summary>The next few tracks. May be empty — most sources do not expose a queue.</summary>
    IReadOnlyList<QueueItem> GetQueue();

    void Play();
    void Pause();
    void Next();
    void Previous();

    /// <summary>
    /// Raised when the media session changes. The web side subscribes through
    /// <c>onNowPlayingChanged(cb)</c>; the bridge translates this event into that
    /// callback, because COM events do not survive the JS boundary cleanly.
    /// </summary>
    event EventHandler<NowPlaying?>? NowPlayingChanged;

    // ---- theming ----

    /// <summary>Id of the theme the shell should load.</summary>
    string GetThemeId();

    /// <summary>Switches theme and persists the choice.</summary>
    void SetThemeId(string themeId);

    /// <summary>Every installed theme, bundled and user-supplied, bundled first.</summary>
    IReadOnlyList<ThemeManifest> GetThemes();

    // ---- background ----

    /// <summary>
    /// What to draw behind the widgets: none (the user's real wallpaper shows
    /// through), or a chosen video/interactive page already resolved to a
    /// URL. An "image" choice never appears here — it's applied straight to
    /// the real Windows wallpaper, and the web layer has nothing to draw for
    /// it.
    /// </summary>
    BackgroundInfo GetBackground();
}
