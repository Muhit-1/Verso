using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using Verso.App.Hosting;
using Verso.App.Storage;
using Verso.Core;
using Verso.Interop;

namespace Verso.App.Windows;

/// <summary>
/// The dashboard: an ordinary top-level window with a WebView2 filling it,
/// showing <c>web/dashboard/index.html</c>.
/// </summary>
/// <remarks>
/// <para>
/// Unlike <see cref="DeskWindow"/>, this does not need <see cref="DesktopSurface"/>'s
/// raw HWND route — that exists only because the desk must be <c>WS_EX_LAYERED</c>
/// and reshaped by a window region, neither of which a normal settings window
/// needs. The standard <c>Microsoft.Web.WebView2.Wpf.WebView2</c> control is
/// enough here.
/// </para>
/// <para>
/// The window has no OS title bar (see DashboardWindow.xaml) but deliberately
/// stays a plain rectangle rather than chasing window-shape transparency —
/// this codebase already spent real effort discovering that WPF and WebView2
/// do not compose cleanly with per-pixel transparency (see docs/desktop-layer.md,
/// "WPF refusing WS_EX_LAYERED"). "Maximize" is therefore faked with
/// <see cref="SystemParameters.WorkArea"/> rather than a true
/// <see cref="WindowState.Maximized"/>, which — on a borderless window — paints
/// over the taskbar unless you additionally hook <c>WM_GETMINMAXINFO</c>. The
/// work-area toggle gets the same visible result without that extra native
/// surface, at the cost of using the primary monitor's work area rather than
/// whichever monitor the window is actually on. Fine for a settings window;
/// would not be fine for the desk.
/// </para>
/// </remarks>
public sealed partial class DashboardWindow : Window
{
    private const string VirtualHost = "verso.example";
    private const string UserDashboardThemesVirtualHost = "verso.user-dashboard-themes.example";

    private readonly DashboardBridge _bridge;
    private Rect? _restoreBounds;

    internal DashboardWindow(
        SettingsStore settings,
        DesktopActions actions,
        ThemeCatalog deskThemes,
        ThemeCatalog dashboardThemes,
        WidgetCatalog widgets)
    {
        InitializeComponent();

        _bridge = new DashboardBridge(actions, deskThemes, dashboardThemes, widgets, settings, this);

        Loaded += async (_, _) => await InitializeWebViewAsync();
    }

    private async Task InitializeWebViewAsync()
    {
        Hosting.StartupLog.Mark("dashboard: creating WebView2 environment");
        var environment = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: VersoPaths.WebViewUserData);
        Hosting.StartupLog.Mark("dashboard: environment ready; ensuring WebView2");

        await Browser.EnsureCoreWebView2Async(environment);
        Hosting.StartupLog.Mark("dashboard: WebView2 ready");

        var core = Browser.CoreWebView2;

        // A settings window is not a browser either.
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsSwipeNavigationEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
#if DEBUG
        core.Settings.AreDevToolsEnabled = true;
#else
        core.Settings.AreDevToolsEnabled = false;
#endif

        core.SetVirtualHostNameToFolderMapping(
            VirtualHost,
            VersoPaths.ResolveWebRoot(),
            CoreWebView2HostResourceAccessKind.Allow);

        // Narrow mapping, same reasoning as DeskWindow's user-themes/user-widgets
        // ones: a page has no business fetching anything from the data root
        // except the one folder it is asking about.
        Directory.CreateDirectory(VersoPaths.UserDashboardThemes);
        core.SetVirtualHostNameToFolderMapping(
            UserDashboardThemesVirtualHost,
            VersoPaths.UserDashboardThemes,
            CoreWebView2HostResourceAccessKind.Allow);

        // The dashboard gets its own bridge — never VersoHost, the widget
        // contract. See the remarks on IVersoDashboard for why.
        core.AddHostObjectToScript("VersoDashboard", _bridge);

        core.WebMessageReceived += OnWebMessage;

        core.NavigationCompleted += (_, _) => Hosting.StartupLog.Mark("dashboard: page navigation completed");
        core.Navigate($"https://{VirtualHost}/dashboard/index.html");
    }

    /// <summary>
    /// Handles what <c>web/dashboard/js/titlebar.js</c> posts for the window
    /// controls it draws in place of an OS title bar.
    /// </summary>
    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;

            if (!root.TryGetProperty("type", out var type) || type.GetString() != "verso:window"
                || !root.TryGetProperty("action", out var actionEl))
            {
                return;
            }

            switch (actionEl.GetString())
            {
                case "drag":
                    TitleBarDrag.Begin(new WindowInteropHelper(this).Handle);
                    break;
                case "minimize":
                    WindowState = WindowState.Minimized;
                    break;
                case "maximize":
                    ToggleMaximize();
                    break;
                case "close":
                    Close();
                    break;
            }
        }
        catch (JsonException)
        {
            // The page could put anything on this channel. Ignore it.
        }
    }

    /// <summary>See the class remarks for why this is a manual bounds swap, not <see cref="WindowState.Maximized"/>.</summary>
    private void ToggleMaximize()
    {
        if (_restoreBounds is { } bounds)
        {
            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;
            _restoreBounds = null;
        }
        else
        {
            _restoreBounds = new Rect(Left, Top, Width, Height);
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Left;
            Top = workArea.Top;
            Width = workArea.Width;
            Height = workArea.Height;
        }
    }
}
