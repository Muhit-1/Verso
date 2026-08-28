using Verso.Core.Models;

namespace Verso.Core.Contracts;

/// <summary>
/// The one door between the dashboard and the machine.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IVersoHost"/> is the widget contract — "the one door between a
/// widget and the machine" — and stays exactly that. The dashboard needs a
/// different, larger set of privileges (enable/disable a widget, hide the
/// taskbar, pick a background file, add or remove a launcher entry) that no
/// widget should ever be able to reach, even accidentally. Rather than widen
/// <see cref="IVersoHost"/> and rely on <c>web/shell/js/widgets.js</c>'s
/// permission scoping to keep widgets away from the new methods, this is a
/// second, separate interface — injected only into the dashboard's own
/// WebView2 as <c>window.chrome.webview.hostObjects.VersoDashboard</c>, never
/// into the desk's.
/// </para>
/// <para>
/// Same shape as <see cref="IVersoHost"/> otherwise: this is the typed
/// contract; <c>DashboardBridge</c> is the <c>[ComVisible(true)]</c> JSON
/// adapter over it, and <c>web/dashboard/js/host.js</c> is what the page
/// actually calls.
/// </para>
/// </remarks>
public interface IVersoDashboard
{
    /// <summary>Dashboard bridge contract version.</summary>
    string HostVersion { get; }

    // ---- desk theme ----

    /// <summary>Every installed desk theme, bundled and user-supplied.</summary>
    IReadOnlyList<ThemeManifest> GetThemes();

    string GetThemeId();

    /// <summary>Switches the desk's theme, persists it, and reloads the desk.</summary>
    void SetThemeId(string themeId);

    // ---- dashboard theme ----

    /// <summary>Every installed dashboard theme, bundled and user-supplied.</summary>
    IReadOnlyList<ThemeManifest> GetDashboardThemes();

    string GetDashboardThemeId();

    /// <summary>
    /// Persists the dashboard's own theme choice. Unlike <see cref="SetThemeId"/>
    /// this does not reload anything — the dashboard applies its own theme
    /// live, in the same page, the moment the user picks it.
    /// </summary>
    void SetDashboardThemeId(string themeId);

    // ---- widgets ----

    /// <summary>Every installed widget, with whether the user has it enabled.</summary>
    IReadOnlyList<WidgetManifest> GetWidgets();

    /// <summary>Turns a widget on or off and reloads the desk.</summary>
    void SetWidgetEnabled(string widgetId, bool enabled);

    /// <summary>Forgets where every panel was dragged to and reloads the desk.</summary>
    void ResetLayout();

    // ---- background ----

    BackgroundInfo GetBackground();

    /// <summary>Opens a native file picker. Applies and persists the choice; reloads the desk.</summary>
    void PickBackground();

    void ClearBackground();

    // ---- desktop cleanup ----

    DesktopCleanupSettings GetDesktopCleanup();

    void SetTaskbarHidden(bool hide);

    void SetDesktopIconsHidden(bool hide);

    // ---- launcher ----

    IReadOnlyList<LauncherInfo> GetLauncherEntries();

    /// <summary>Opens a native file picker and adds the chosen app; reloads the desk.</summary>
    void AddLauncherEntry();

    void RemoveLauncherEntry(string id);
}
