using System.Diagnostics;
using System.IO;
using System.Windows;
using H.NotifyIcon;
using Verso.Core;
using Verso.App.Hosting;
using Verso.App.Storage;
using Verso.App.Windows;

namespace Verso.App;

public partial class App : Application
{
    private const string SingleInstanceMutexName = @"Global\Verso.SingleInstance";

    private Mutex? _instanceMutex;
    private TaskbarIcon? _tray;
    private SettingsStore? _settings;
    private ScriptBridge? _bridge;
    private PreviewWindow? _preview;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // One wallpaper per machine. A second copy would fight the first for
        // the WorkerW and you would get flicker rather than an error.
        _instanceMutex = new Mutex(true, SingleInstanceMutexName, out var isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        VersoPaths.EnsureCreated();

        _settings = new SettingsStore();
        _bridge = new ScriptBridge(
            new FileTaskStore(),
            new ThemeCatalog(),
            _settings);

        _tray = BuildTrayIcon();
    }

    private TaskbarIcon BuildTrayIcon()
    {
        var menu = new System.Windows.Controls.ContextMenu();

        menu.Items.Add(MenuItem("Widget desk (preview)", ShowPreview));
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(MenuItem("Open data folder", () => OpenInExplorer(VersoPaths.DataRoot)));
        menu.Items.Add(MenuItem("Open themes folder", () => OpenInExplorer(VersoPaths.UserThemes)));
        menu.Items.Add(new System.Windows.Controls.Separator());
        menu.Items.Add(MenuItem("Exit", Shutdown));

        var icon = new TaskbarIcon
        {
            ToolTipText = "Verso",
            ContextMenu = menu,
            IconSource = new System.Windows.Media.Imaging.BitmapImage(
                new Uri("pack://application:,,,/verso.ico"))
        };

        icon.TrayLeftMouseUp += (_, _) => ShowPreview();
        icon.ForceCreate();
        return icon;
    }

    private static System.Windows.Controls.MenuItem MenuItem(string header, Action onClick)
    {
        var item = new System.Windows.Controls.MenuItem { Header = header };
        item.Click += (_, _) => onClick();
        return item;
    }

    /// <summary>
    /// Opens the web layer in a normal window.
    /// </summary>
    /// <remarks>
    /// This is scaffolding, not the product. It exists so the whole web layer —
    /// shell, themes and widgets — can be developed against the real WebView2
    /// and the real bridge before the wallpaper window exists. Phase 1 replaces
    /// it with a window parented into WorkerW; the WebView2 setup carries over
    /// unchanged.
    /// </remarks>
    private void ShowPreview()
    {
        if (_bridge is null)
        {
            return;
        }

        if (_preview is { IsLoaded: true })
        {
            _preview.Activate();
            return;
        }

        _preview = new PreviewWindow(_bridge);
        _preview.Closed += (_, _) => _preview = null;
        _preview.Show();
    }

    private static void OpenInExplorer(string path)
    {
        Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _bridge?.Dispose();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
