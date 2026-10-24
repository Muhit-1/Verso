using Microsoft.Web.WebView2.Core;

namespace Verso.App.Hosting;

/// <summary>
/// Locks a WebView2 down to the pages Verso itself serves. Applied to every window that hosts the web layer.
/// </summary>
/// <remarks>
/// <para>
/// The desk shows content Verso did not write: an interactive wallpaper is somebody's HTML page, and a user can
/// install widgets. None of that has any reason to navigate the window away, open a window, ask for the camera or
/// download a file — and a page that navigated the top frame to a site of its choosing would be a page with the host
/// bridge (<c>VersoHost</c>, <c>VersoDashboard</c>) in its hands. So the rules are simple and the same everywhere:
/// </para>
/// <list type="bullet">
/// <item>the top frame may only ever be one of Verso's own virtual hosts;</item>
/// <item>frames may load Verso's virtual hosts and nothing from the internet;</item>
/// <item>no new windows, no downloads, no permission prompts (all denied).</item>
/// </list>
/// </remarks>
internal static class WebViewGuard
{
    /// <summary>The origin of Verso's own pages. The only top-level page a window may show.</summary>
    public const string AppHost = "verso.example";

    private static readonly string[] FrameHosts =
    [
        AppHost,
        "verso.user-themes.example",
        "verso.user-widgets.example",
        "verso.user-dashboard-themes.example",
        ScriptBridge.BackgroundVirtualHost
    ];

    /// <summary>Whether the top-level frame may navigate to <paramref name="uri"/>.</summary>
    internal static bool IsAllowedTopLevel(string? uri)
        => Uri.TryCreate(uri, UriKind.Absolute, out var u)
           && u.Scheme == Uri.UriSchemeHttps
           && string.Equals(u.Host, AppHost, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a frame (a widget, a wallpaper page) may load <paramref name="uri"/>.</summary>
    internal static bool IsAllowedFrame(string? uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var u))
        {
            return false;
        }

        // srcdoc and blank frames carry no network address; they cannot reach anything by themselves
        if (u.Scheme == "about")
        {
            return true;
        }

        return u.Scheme == Uri.UriSchemeHttps
               && FrameHosts.Contains(u.Host, StringComparer.OrdinalIgnoreCase);
    }

    public static void Apply(CoreWebView2 core, string windowName)
    {
        core.NavigationStarting += (_, e) =>
        {
            if (!IsAllowedTopLevel(e.Uri))
            {
                e.Cancel = true;
                Log.Warn($"{windowName}: blocked top-level navigation to {Describe(e.Uri)}");
            }
        };

        core.FrameNavigationStarting += (_, e) =>
        {
            if (!IsAllowedFrame(e.Uri))
            {
                e.Cancel = true;
                Log.Warn($"{windowName}: blocked frame navigation to {Describe(e.Uri)}");
            }
        };

        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            Log.Warn($"{windowName}: blocked a new window to {Describe(e.Uri)}");
        };

        core.PermissionRequested += (_, e) =>
        {
            e.State = CoreWebView2PermissionState.Deny;
        };

        core.DownloadStarting += (_, e) =>
        {
            e.Cancel = true;
            e.Handled = true;
        };
    }

    /// <summary>Scheme and host only: a query string can carry anything and has no place in a log.</summary>
    private static string Describe(string? uri)
        => Uri.TryCreate(uri, UriKind.Absolute, out var u) ? $"{u.Scheme}://{u.Host}" : "(unparseable address)";
}
