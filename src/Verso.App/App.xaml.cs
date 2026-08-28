using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using Verso.App.Hosting;
using Verso.App.Media;
using Verso.App.Storage;
using Verso.App.Windows;
using Verso.Core;
using Verso.Interop;

namespace Verso.App;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Global\Verso.SingleInstance";

    private Mutex? _instanceMutex;
    private TaskbarIcon? _tray;
    private SettingsStore? _settings;
    private ScriptBridge? _bridge;
    private DeskController? _desk;
    private DesktopActions? _actions;
    private ThemeCatalog? _themes;
    private ThemeCatalog? _dashboardThemes;
    private WidgetCatalog? _widgets;
    private FileLayoutStore? _layout;
    private SmtcNowPlayingSource? _media;
    private MenuItem? _pauseItem;

    /// <summary>
    /// The dashboard is a singleton: opening it twice from the tray activates
    /// the one already open rather than stacking a second copy.
    /// </summary>
    private DashboardWindow? _dashboardWindow;

    /// <summary>
    /// Set once this process has actually taken the single-instance mutex.
    /// A second copy shuts down before it ever touches the taskbar or desktop
    /// icons, and <see cref="OnExit"/> must not restore them on its way out —
    /// that would clobber whatever the real, first instance has hidden.
    /// </summary>
    private bool _isPrimaryInstance;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // One desk per machine. A second copy would fight the first for the
        // bottom of the Z-order and you would get flicker rather than an error.
        _instanceMutex = new Mutex(true, SingleInstanceMutexName, out var isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        _isPrimaryInstance = true;

        VersoPaths.EnsureCreated();

        _settings = new SettingsStore();
        _themes = new ThemeCatalog(VersoPaths.BundledThemes, VersoPaths.UserThemes);
        _dashboardThemes = new ThemeCatalog(VersoPaths.BundledDashboardThemes, VersoPaths.UserDashboardThemes);
        _widgets = new WidgetCatalog();
        _layout = new FileLayoutStore();

        _media = new SmtcNowPlayingSource();
        await _media.StartAsync();

        _bridge = new ScriptBridge(new FileTaskStore(), _themes, _widgets, _settings, _layout, _media);

        ApplyDesktopCleanup();

        _desk = new DeskController(_bridge, _settings);
        _actions = new DesktopActions(_settings, _desk, _layout);

        _tray = BuildTrayIcon();

        await _desk.StartAsync();

        RefreshTray();
    }

    private TaskbarIcon BuildTrayIcon()
    {
        var menu = new ContextMenu();

        menu.Items.Add(MenuItem("Settings…", OpenSettingsWindow));
        menu.Items.Add(new Separator());
        _pauseItem = MenuItem("Hide the desk", TogglePaused);
        menu.Items.Add(_pauseItem);
        menu.Items.Add(MenuItem("Reload", () => _desk?.ReloadWeb()));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("Set background…", () => _actions?.PickBackground(null)));
        menu.Items.Add(MenuItem("Clear background", () => _actions?.ClearBackground()));
        menu.Items.Add(new Separator());
        menu.Items.Add(CheckableMenuItem(
            "Hide taskbar", _settings!.Current.DesktopCleanup.HideTaskbar,
            hide => _actions?.SetTaskbarHidden(hide)));
        menu.Items.Add(CheckableMenuItem(
            "Hide desktop icons", _settings.Current.DesktopCleanup.HideDesktopIcons,
            hide => _actions?.SetDesktopIconsHidden(hide)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("Open data folder", () => OpenInExplorer(VersoPaths.DataRoot)));
        menu.Items.Add(MenuItem("Open themes folder", () => OpenInExplorer(VersoPaths.UserThemes)));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("Exit", Shutdown));

        var icon = new TaskbarIcon
        {
            ToolTipText = "Verso",
            ContextMenu = menu,
            IconSource = new System.Windows.Media.Imaging.BitmapImage(
                new Uri("pack://application:,,,/verso.ico"))
        };

        // Left-click opens the dashboard, the way most tray apps behave.
        // Right-click still opens this ContextMenu, unchanged.
        icon.TrayLeftMouseUp += (_, _) =>
        {
            RefreshTray();
            OpenSettingsWindow();
        };
        icon.ForceCreate();
        return icon;
    }

    private void TogglePaused()
    {
        if (_desk is null)
        {
            return;
        }

        _desk.SetUserPaused(_desk.IsShown);
        RefreshTray();
    }

    /// <summary>Applies the persisted taskbar/icon choice at startup, before the tray even appears.</summary>
    private void ApplyDesktopCleanup()
    {
        var cleanup = _settings!.Current.DesktopCleanup;
        DesktopChrome.SetTaskbarVisible(!cleanup.HideTaskbar);
        DesktopChrome.SetDesktopIconsVisible(!cleanup.HideDesktopIcons);
    }

    /// <summary>Opens the dashboard, or brings the one already open to the front.</summary>
    private void OpenSettingsWindow()
    {
        if (_settings is null || _actions is null || _themes is null || _dashboardThemes is null || _widgets is null)
        {
            return;
        }

        if (_dashboardWindow is null)
        {
            _dashboardWindow = new DashboardWindow(_settings, _actions, _themes, _dashboardThemes, _widgets);
            _dashboardWindow.Closed += (_, _) => _dashboardWindow = null;
            _dashboardWindow.Show();
        }
        else
        {
            _dashboardWindow.Activate();
        }
    }

    private void RefreshTray()
    {
        if (_tray is null || _desk is null)
        {
            return;
        }

        _tray.ToolTipText = _desk.Describe();

        if (_pauseItem is not null)
        {
            _pauseItem.Header = _desk.IsShown ? "Hide the desk" : "Show the desk";
        }
    }

    private static MenuItem MenuItem(string header, Action onClick)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => onClick();
        return item;
    }

    /// <summary>
    /// A menu item with a checkmark. WPF flips <see cref="MenuItem.IsChecked"/>
    /// before <c>Click</c> fires, so <paramref name="onToggle"/> is handed the
    /// new state directly rather than having to read and invert it.
    /// </summary>
    private static MenuItem CheckableMenuItem(string header, bool isChecked, Action<bool> onToggle)
    {
        var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = isChecked };
        item.Click += (_, _) => onToggle(item.IsChecked);
        return item;
    }

    private static void OpenInExplorer(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Only the instance that actually hid them puts them back. A second
        // copy shutting itself down must not restore what the first,
        // still-running instance deliberately hid.
        if (_isPrimaryInstance)
        {
            DesktopChrome.SetTaskbarVisible(true);
            DesktopChrome.SetDesktopIconsVisible(true);
        }

        _desk?.Dispose();
        _tray?.Dispose();
        _bridge?.Dispose();
        _media?.Dispose();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
