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
    private Dock.DockController? _dock;
    private ThemeCatalog? _themes;
    private ThemeCatalog? _dashboardThemes;
    private WidgetCatalog? _widgets;
    private FileLayoutStore? _layout;
    private SmtcNowPlayingSource? _media;
    private MenuItem? _pauseItem;
    private MenuItem? _styleItem;
    private MenuItem? _taskbarItem;
    private MenuItem? _iconsItem;
    private LauncherLibrary? _library;
    private NotesLibrary? _notes;
    private TextEditorWindow? _editor;
    private AppPickerWindow? _picker;

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

        StartupLog.Mark("OnStartup begins (process is up)");
        VersoPaths.EnsureCreated();

        _settings = new SettingsStore();
        _themes = new ThemeCatalog(VersoPaths.BundledThemes, VersoPaths.UserThemes);
        _dashboardThemes = new ThemeCatalog(VersoPaths.BundledDashboardThemes, VersoPaths.UserDashboardThemes);
        _widgets = new WidgetCatalog();
        _layout = new FileLayoutStore();

        _library = new LauncherLibrary(_settings);
        _notes = new NotesLibrary(_settings, new FileLayoutStore(VersoPaths.OutpostNotesFile));
        StartupLog.Mark("settings + catalogs ready");
        _media = new SmtcNowPlayingSource();
        await _media.StartAsync();
        StartupLog.Mark("SMTC (now playing) started");

        _bridge = new ScriptBridge(new FileTaskStore(), new FileNoteStore(), new FileAgendaStore(), _themes, _widgets, _settings, _layout, new FileLayoutStore(VersoPaths.OutpostFile), _media, _library, _notes);

        ApplyDesktopCleanup();
        _bridge.OpenDashboardRequested += OpenSettingsWindow;
        _bridge.EditRequested += OpenEditor;
        _library.PickerRequested += OpenPicker;
        _bridge.DesktopCleanupRequested += (which, hide) =>
        {
            if (which == "taskbar")
            {
                _actions?.SetTaskbarHidden(hide);
            }
            else
            {
                _actions?.SetDesktopIconsHidden(hide);
            }

            SyncTrayChecks();
        };

        _desk = new DeskController(_bridge, _settings);
        _actions = new DesktopActions(_settings, _desk, _layout, _library, _notes);
        _dock = new Dock.DockController(_settings);
        _actions.DockChanged += _dock.Apply;
        ApplyDesktopCleanup();
        _dock.Apply();

        _tray = BuildTrayIcon();

        StartupLog.Mark("tray built; desk start begins");
        await _desk.StartAsync();
        StartupLog.Mark("desk started (windows created, web pages navigating)");
        TextEditorWindow.WarmUp();

        RefreshTray();

        // The dashboard, not the desk's widgets, is what greets a launch —
        // that's where widgets/background/theme actually get chosen. The
        // desk itself still starts above, since it's the real wallpaper.
        StartupLog.Mark("opening the dashboard window");
        OpenSettingsWindow();
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
        menu.Items.Add(MenuItem("Set background…", () => _ = _actions?.PickBackground(null)));
        menu.Items.Add(MenuItem("Clear background", () => _ = _actions?.ClearBackground()));
        menu.Items.Add(new Separator());
        _styleItem = CheckableMenuItem(
            "Outpost desk (widgets)", _settings!.Current.DeskStyle == "outpost",
            on => _ = _actions?.SetDeskStyle(on ? "outpost" : "classic"));
        _taskbarItem = CheckableMenuItem(
            "Hide taskbar", _settings.Current.DesktopCleanup.HideTaskbar,
            hide => _actions?.SetTaskbarHidden(hide));
        _iconsItem = CheckableMenuItem(
            "Hide desktop icons", _settings.Current.DesktopCleanup.HideDesktopIcons,
            hide => _actions?.SetDesktopIconsHidden(hide));
        menu.Items.Add(_styleItem);
        menu.Items.Add(_taskbarItem);
        menu.Items.Add(_iconsItem);
        // these three can also be changed from the dashboard and the desk menu; show the truth whenever the tray opens
        menu.Opened += (_, _) => SyncTrayChecks();
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
        DesktopChrome.SetTaskbarVisible(!(cleanup.HideTaskbar || _settings.Current.Dock.Enabled));
        DesktopChrome.SetDesktopIconsVisible(!cleanup.HideDesktopIcons);
    }

    /// <summary>
    /// Opens the pop-up editor over a note or a box name. The desk page cannot take keyboard input, so typing happens in
    /// a real window; its text goes back into the notes / launcher library.
    /// </summary>
    private void OpenEditor(EditRequest request)
    {
        if (_notes is null || _library is null || _bridge is null)
        {
            return;
        }

        void Apply(string value)
        {
            if (request.Kind == "note")
            {
                _notes.SetHtml(request.Id, value);
            }
            else
            {
                _library.RenameBox(request.Id, value);
            }
        }

        if (_editor is null)
        {
            // the first editor is built off-screen and kept for the next one (see TextEditorWindow)
            var created = new TextEditorWindow();
            created.Retired += () =>
            {
                if (ReferenceEquals(_editor, created))
                {
                    _editor = null;
                }
            };
            _editor = created;
            created.Show();
        }

        _editor.Begin(request, Apply, Apply, () => _bridge.RaiseEditorClosed());
    }

    /// <summary>
    /// Opens the installed-apps picker for a box. The first picker is built off-screen and kept for the next time
    /// (see <see cref="AppPickerWindow"/>).
    /// </summary>
    private void OpenPicker(string boxId, System.Drawing.Rectangle? anchor)
    {
        if (_library is null)
        {
            return;
        }

        if (_picker is null)
        {
            var created = new AppPickerWindow(_library);
            created.Retired += () =>
            {
                if (ReferenceEquals(_picker, created))
                {
                    _picker = null;
                }
            };
            _picker = created;
            created.Show();
        }

        _picker.Begin(boxId, anchor);
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
    /// <summary>Makes the tray's check marks match the settings (they can be changed from several places).</summary>
    private void SyncTrayChecks()
    {
        if (_settings is null)
        {
            return;
        }

        if (_styleItem is not null) _styleItem.IsChecked = _settings.Current.DeskStyle == "outpost";
        if (_taskbarItem is not null) _taskbarItem.IsChecked = _settings.Current.DesktopCleanup.HideTaskbar;
        if (_iconsItem is not null) _iconsItem.IsChecked = _settings.Current.DesktopCleanup.HideDesktopIcons;
    }

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
        _dock?.Dispose();

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
