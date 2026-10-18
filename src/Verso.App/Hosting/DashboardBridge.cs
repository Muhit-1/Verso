using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using Verso.App.Storage;
using Verso.Core.Models;

namespace Verso.App.Hosting;

/// <summary>
/// The object that appears in JavaScript as
/// <c>window.chrome.webview.hostObjects.VersoDashboard</c>.
/// </summary>
/// <remarks>
/// Same JSON-string-adapter role for <c>IVersoDashboard</c> that
/// <see cref="ScriptBridge"/> plays for <c>IVersoHost</c> — see the remarks on
/// <c>IVersoDashboard</c> for why this is a second bridge rather than new
/// methods bolted onto <see cref="ScriptBridge"/>. Every method here delegates
/// straight to <see cref="DesktopActions"/> or a catalog; there is no logic of
/// its own, on purpose — <see cref="DesktopActions"/> stays the one place that
/// knows how to change a setting, whether the caller is the tray, this bridge,
/// or (previously) the WPF settings window it replaces.
/// </remarks>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.AutoDual)]
public sealed class DashboardBridge
{
    public const string ContractVersion = "0.1.0";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly DesktopActions _actions;
    private readonly ThemeCatalog _deskThemes;
    private readonly ThemeCatalog _dashboardThemes;
    private readonly WidgetCatalog _widgets;
    private readonly SettingsStore _settings;
    private readonly Window? _owner;

    internal DashboardBridge(
        DesktopActions actions,
        ThemeCatalog deskThemes,
        ThemeCatalog dashboardThemes,
        WidgetCatalog widgets,
        SettingsStore settings,
        Window? owner)
    {
        _actions = actions;
        _deskThemes = deskThemes;
        _dashboardThemes = dashboardThemes;
        _widgets = widgets;
        _settings = settings;
        _owner = owner;
    }

    public string HostVersion() => ContractVersion;

    // ---- desk theme -------------------------------------------------------

    public string GetThemes() => JsonSerializer.Serialize(_deskThemes.Scan(), Json);

    public string GetThemeId() => _settings.Current.ThemeId;

    public void SetThemeId(string themeId) => _actions.SetTheme(themeId);

    // ---- dashboard theme ----------------------------------------------------

    public string GetDashboardThemes() => JsonSerializer.Serialize(_dashboardThemes.Scan(), Json);

    public string GetDashboardThemeId() => _settings.Current.DashboardThemeId;

    /// <summary>
    /// Persistence only — no reload. The dashboard applies its own theme live,
    /// in the page that is already running, the moment it is picked.
    /// </summary>
    public void SetDashboardThemeId(string themeId)
    {
        if (string.IsNullOrWhiteSpace(themeId))
        {
            return;
        }

        _settings.Update(s => s.DashboardThemeId = themeId);
    }

    // ---- desk style and the Outpost palette ------------------------------------

    /// <returns>"outpost" or "classic" — which kind of desk the Theme page should describe.</returns>
    public string GetDeskStyle() => _settings.Current.DeskStyle;

    public string GetOutpostThemes() => JsonSerializer.Serialize(OutpostPalettes.Describe(_settings.Current), Json);

    public string GetOutpostTheme() => OutpostPalettes.Chosen(_settings.Current);

    public void SetOutpostTheme(string id) => _actions.SetOutpostTheme(id);

    // ---- widgets ------------------------------------------------------------

    public string GetWidgets()
    {
        // The Outpost desk has its own widgets, each with its own switch.
        if (_settings.Current.DeskStyle == "outpost")
        {
            return JsonSerializer.Serialize(OutpostWidgetCatalog.Describe(_settings.Current), Json);
        }

        var manifests = _widgets.Scan();
        WidgetCatalog.ApplyEnabled(manifests, _settings.Current.Widgets.Disabled);
        return JsonSerializer.Serialize(manifests, Json);
    }

    public void SetWidgetEnabled(string widgetId, bool enabled) => _actions.SetWidgetEnabled(widgetId, enabled);

    public void ResetLayout() => _actions.ResetLayout();

    // ---- background ---------------------------------------------------------

    public string GetBackground()
    {
        var background = _settings.Current.Background;
        string? url = null;

        if (background.Kind is "video" or "gif" or "interactive" && !string.IsNullOrWhiteSpace(background.Source))
        {
            url = $"https://{ScriptBridge.BackgroundVirtualHost}/{Uri.EscapeDataString(Path.GetFileName(background.Source))}";
        }

        return JsonSerializer.Serialize(new { kind = background.Kind, url }, Json);
    }

    public void PickBackground() => _ = _actions.PickBackground(_owner);

    public void ClearBackground() => _ = _actions.ClearBackground();

    // ---- desktop cleanup ------------------------------------------------------

    public string GetDesktopCleanup() => JsonSerializer.Serialize(_settings.Current.DesktopCleanup, Json);

    public void SetTaskbarHidden(bool hide) => _actions.SetTaskbarHidden(hide);

    public void SetDesktopIconsHidden(bool hide) => _actions.SetDesktopIconsHidden(hide);

    // ---- launcher -------------------------------------------------------------

    public string GetLauncherEntries()
    {
        var info = _settings.Current.Launcher.Entries
            .Select(e => new LauncherInfo { Id = e.Id, Name = e.Name })
            .ToList();

        return JsonSerializer.Serialize(info, Json);
    }

    public void AddLauncherEntry() => _actions.AddLauncherEntry(_owner);

    public void RemoveLauncherEntry(string id) => _actions.RemoveLauncherEntry(id);

    // ---- app library: apps sorted into boxes ------------------------------------

    /// <returns>JSON array of boxes <c>{id, name, builtIn, apps:[{id, name}]}</c>, Recent first.</returns>
    public string GetApps() => JsonSerializer.Serialize(_actions.Library.Describe(), Json);

    /// <returns>A <c>data:image/png</c> URL for the app's icon, or <c>""</c>.</returns>
    public string GetAppIcon(string id) => _actions.Library.GetIcon(id);

    /// <summary>Opens the file picker; new apps land in the given box ("recent" if empty).</summary>
    public void AddApp(string boxId) => _actions.Library.PickAndAdd(boxId, _owner);

    public void RemoveApp(string id) => _actions.Library.RemoveApp(id);

    public void MoveApp(string id, string boxId) => _actions.Library.MoveApp(id, boxId);

    public string AddBox() => _actions.Library.AddBox();

    public void RenameBox(string id, string name) => _actions.Library.RenameBox(id, name);

    public void DeleteBox(string id) => _actions.Library.DeleteBox(id);

    // ---- sticky notes: the dashboard only counts and creates; typing happens on the desk -----------

    public int GetNoteCount() => _actions.Notes.Describe().Count;

    public string NewNote() => _actions.Notes.NewNote();
}
