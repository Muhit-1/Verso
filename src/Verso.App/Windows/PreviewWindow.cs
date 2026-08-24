using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Verso.App.Hosting;
using Verso.Core;

namespace Verso.App.Windows;

/// <summary>
/// The web layer in an ordinary window, with the real bridge attached.
/// </summary>
/// <remarks>
/// Scaffolding for Phases 0–2. The WebView2 configuration here — user data
/// folder, virtual host mapping, the host object — is the same configuration
/// the wallpaper window will use, so none of it is throwaway. Only the window
/// itself is.
/// </remarks>
internal sealed class PreviewWindow : Window
{
    /// <summary>
    /// The web layer is served from https://verso.local/ rather than from
    /// file:// so that it gets a real origin. file:// pages have no localStorage
    /// worth the name, no fetch of sibling files, and no module scripts.
    /// </summary>
    private const string VirtualHost = "verso.local";

    private readonly ScriptBridge _bridge;
    private readonly WebView2 _web = new();

    public PreviewWindow(ScriptBridge bridge)
    {
        _bridge = bridge;

        Title = "Verso — widget desk (preview)";
        Width = 1440;
        Height = 900;
        MinWidth = 480;
        MinHeight = 360;
        Background = System.Windows.Media.Brushes.Black;
        Content = _web;

        Loaded += async (_, _) => await InitialiseAsync();
    }

    private async Task InitialiseAsync()
    {
        // Keeping WebView2's cache out of the settings folder means "reset my
        // settings" never means "log me out of everything".
        var environment = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: VersoPaths.WebViewUserData);

        await _web.EnsureCoreWebView2Async(environment);

        var core = _web.CoreWebView2;

        // A wallpaper is not a browser.
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsSwipeNavigationEnabled = false;
#if DEBUG
        core.Settings.AreDevToolsEnabled = true;
#else
        core.Settings.AreDevToolsEnabled = false;
#endif

        var webRoot = ResolveWebRoot();
        core.SetVirtualHostNameToFolderMapping(
            VirtualHost,
            webRoot,
            CoreWebView2HostResourceAccessKind.Allow);

        // This one line is the entire host integration. Everything the widgets
        // can do to the machine, they do through this object.
        core.AddHostObjectToScript("VersoHost", _bridge);

        core.Navigate($"https://{VirtualHost}/shell/index.html");
    }

    /// <summary>
    /// Where the web layer lives.
    /// </summary>
    /// <remarks>
    /// An installed build has it copied next to the executable. Running from
    /// source, that copy is also there — but preferring the repo's own web
    /// folder when it is findable means editing a theme and hitting reload
    /// works without a rebuild.
    /// </remarks>
    private static string ResolveWebRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && directory is not null; i++, directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "web", "shell", "index.html");
            if (File.Exists(candidate))
            {
                return Path.Combine(directory.FullName, "web");
            }
        }

        return VersoPaths.BundledWeb;
    }
}
