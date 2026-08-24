using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using Verso.App.Hosting;
using Verso.App.Storage;
using Verso.Core;

namespace Verso.App;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Global\Verso.SingleInstance";

    private Mutex? _instanceMutex;
    private TaskbarIcon? _tray;
    private SettingsStore? _settings;
    private ScriptBridge? _bridge;
    private DeskController? _desk;
    private MenuItem? _pauseItem;

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

        VersoPaths.EnsureCreated();

        _settings = new SettingsStore();
        _bridge = new ScriptBridge(new FileTaskStore(), new ThemeCatalog(), _settings);

        _tray = BuildTrayIcon();

        _desk = new DeskController(_bridge, _settings);
        await _desk.StartAsync();

        RefreshTray();
    }

    private TaskbarIcon BuildTrayIcon()
    {
        var menu = new ContextMenu();

        _pauseItem = MenuItem("Hide the desk", TogglePaused);
        menu.Items.Add(_pauseItem);
        menu.Items.Add(MenuItem("Reload", () => _desk?.ReloadWeb()));
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

        icon.TrayLeftMouseUp += (_, _) => RefreshTray();
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

    private static void OpenInExplorer(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _desk?.Dispose();
        _tray?.Dispose();
        _bridge?.Dispose();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
