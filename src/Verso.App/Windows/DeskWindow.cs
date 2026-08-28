using System.Drawing;
using System.IO;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Verso.App.Hosting;
using Verso.App.Storage;
using Verso.Core;
using Verso.Interop;

namespace Verso.App.Windows;

/// <summary>How a desk sits on the desktop.</summary>
internal enum DeskMode
{
    /// <summary>
    /// Verso draws over the user's existing wallpaper. Everything it is not
    /// actually drawing is painted in the colour key, which the surface turns
    /// fully transparent and click-through — so the real wallpaper and the
    /// desktop icons show through and stay usable. The default, and the only
    /// mode that works on every Windows build.
    /// </summary>
    Overlay,

    /// <summary>
    /// Verso is the wallpaper: the stage fills the screen. Only chosen where
    /// <see cref="DesktopLayer.DetectLayout"/> reports
    /// <see cref="DesktopLayer.DesktopLayout.Splittable"/>, so it can go into
    /// the WorkerW and sit genuinely behind the icons.
    /// </summary>
    Wallpaper
}

/// <summary>
/// One desk per display: a WebView2 showing the web layer, placed on the
/// desktop.
/// </summary>
/// <remarks>
/// <para>
/// Not a WPF window. See <see cref="DesktopSurface"/> for why — the short
/// version is that WPF will not let a window be <c>WS_EX_LAYERED</c>, and
/// without that there is no colour key and therefore no way to leave the
/// desktop icons visible.
/// </para>
/// <para>
/// WebView2 is hosted through the Win32 API
/// (<c>CreateCoreWebView2ControllerAsync(hwnd)</c>) rather than the WPF
/// control, which is what makes it composite inside the layered window.
/// </para>
/// </remarks>
internal sealed class DeskWindow : IDisposable
{
    /// <summary>
    /// The web layer is served from https://verso.local/ rather than file://
    /// so it gets a real origin. file:// pages have no useful localStorage,
    /// cannot fetch sibling files, and cannot load module scripts.
    /// </summary>
    private const string VirtualHost = "verso.local";

    private readonly ScriptBridge _bridge;
    private readonly SettingsStore _settings;
    private readonly DeskMode _mode;
    private readonly DesktopSurface _surface;

    private CoreWebView2Controller? _controller;
    private bool _usingWorkerW;
    private bool _disposed;

    public DeskWindow(
        ScriptBridge bridge,
        SettingsStore settings,
        DeskMode mode,
        DesktopLayer.DesktopLayout layout,
        Rectangle bounds)
    {
        _bridge = bridge;
        _settings = settings;
        _mode = mode;

        _surface = new DesktopSurface(bounds.X, bounds.Y, bounds.Width, bounds.Height);

        // The WorkerW route is only taken when the desktop is genuinely
        // splittable. On builds where it is not, attaching still reports success
        // and draws nothing — the layout check is what stops us shipping an
        // invisible wallpaper.
        if (mode == DeskMode.Wallpaper && layout == DesktopLayer.DesktopLayout.Splittable)
        {
            var outcome = DesktopLayer.Attach(_surface.Handle);
            if (outcome.Success)
            {
                _usingWorkerW = true;
                PlacementDescription = $"WorkerW ({outcome.Strategy})";
            }
        }

        PlacementDescription ??= mode == DeskMode.Wallpaper
            ? "bottom-most layer (no WorkerW on this build)"
            : "bottom-most layer (overlay)";
    }

    /// <summary>What actually happened when this desk was placed. For the tray tooltip.</summary>
    public string? PlacementDescription { get; private set; }

    public async Task InitialiseAsync()
    {
        var environment = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: VersoPaths.WebViewUserData);

        _controller = await environment.CreateCoreWebView2ControllerAsync(_surface.Handle);

        var (width, height) = _surface.ClientSize;
        _controller.Bounds = new Rectangle(0, 0, width, height);

        // Never actually seen in overlay mode — the window region excludes
        // every pixel the page does not draw in. It matters for the instant
        // between the controller appearing and the first region arriving.
        _controller.DefaultBackgroundColor = Color.Transparent;

        var core = _controller.CoreWebView2;

        // A wallpaper is not a browser.
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
            ResolveWebRoot(),
            CoreWebView2HostResourceAccessKind.Allow);

        ApplyBackgroundMapping();

        // The entire host integration, in one line.
        core.AddHostObjectToScript("VersoHost", _bridge);

        // Starting WebView2 can raise the surface. Put it back once there is
        // something to look at.
        core.NavigationCompleted += (_, _) => ReassertPlacement();

        // The page tells us which rectangles it occupies; we reshape the window
        // to exactly those. See SurfaceRegion for why this rather than a colour
        // key or per-pixel alpha.
        core.WebMessageReceived += OnWebMessage;

        var layer = _mode == DeskMode.Wallpaper ? "wallpaper" : "overlay";
        core.Navigate($"https://{VirtualHost}/shell/index.html?layer={layer}");

        _controller.IsVisible = true;
        ReassertPlacement();
    }

    /// <summary>
    /// Reshapes the window to the rectangles the page says it occupies.
    /// </summary>
    /// <remarks>
    /// Overlay mode only. In wallpaper mode the desk is the whole screen and a
    /// region would be a full-screen rectangle for no benefit.
    ///
    /// Messages arrive from the page and are treated strictly as data: shape is
    /// validated, values are clamped to the surface, and anything malformed is
    /// dropped rather than trusted.
    /// </remarks>
    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (_mode != DeskMode.Overlay)
        {
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;

            if (!root.TryGetProperty("type", out var type)
                || type.GetString() != "verso:regions"
                || !root.TryGetProperty("panels", out var panels)
                || panels.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            var (surfaceWidth, surfaceHeight) = _surface.ClientSize;
            var shapes = new List<SurfaceRegion.Panel>(panels.GetArrayLength());

            foreach (var panel in panels.EnumerateArray())
            {
                var x = Clamp(panel, "x", surfaceWidth);
                var y = Clamp(panel, "y", surfaceHeight);
                var w = Clamp(panel, "w", surfaceWidth);
                var h = Clamp(panel, "h", surfaceHeight);
                var r = Clamp(panel, "r", 64);

                if (w > 0 && h > 0)
                {
                    shapes.Add(new SurfaceRegion.Panel(x, y, w, h, r));
                }
            }

            SurfaceRegion.Apply(_surface.Handle, shapes);
            _hasRegion = true;
        }
        catch (JsonException)
        {
            // A widget could put anything on this channel. Ignore it.
        }
    }

    private static int Clamp(JsonElement panel, string name, int max)
        => panel.TryGetProperty(name, out var value) && value.TryGetInt32(out var n)
            ? Math.Clamp(n, 0, max)
            : 0;

    private bool _hasRegion;

    /// <summary>Re-asserts the desk's place at the bottom of the Z-order.</summary>
    /// <remarks>
    /// A WorkerW child keeps its place on its own; a bottom-most window can be
    /// raised by another app activating, by Show Desktop, or by a resolution
    /// change, so it needs putting back.
    /// </remarks>
    public void ReassertPlacement()
    {
        if (_disposed || _usingWorkerW)
        {
            return;
        }

        // Never while the user is working in it. Clicking a widget raises the
        // desk, which is what makes the task field focusable at all; putting it
        // straight back down would take it away mid-sentence.
        if (BottomMostLayer.IsForeground(_surface.Handle))
        {
            return;
        }

        _surface.PinToBottom();
    }

    /// <summary>
    /// Stops or resumes rendering.
    /// </summary>
    /// <remarks>
    /// Hiding is what actually saves the power — an occluded but visible
    /// WebView2 still composites. <c>TrySuspendAsync</c> then lets Chromium
    /// release its render process, which is where the memory goes. It only
    /// succeeds when the controller is already hidden, hence the order.
    /// </remarks>
    public async Task SetRenderingAsync(bool rendering)
    {
        if (_disposed || _controller is null)
        {
            return;
        }

        if (rendering)
        {
            _surface.SetVisible(true);
            _controller.IsVisible = true;
            _controller.CoreWebView2.Resume();
            ReassertPlacement();

            // The page republishes on resize, but a resume is not a resize.
            if (_hasRegion)
            {
                _controller.CoreWebView2.PostWebMessageAsJson("{\"type\":\"verso:republish\"}");
            }
        }
        else
        {
            _controller.IsVisible = false;
            _surface.SetVisible(false);
            await _controller.CoreWebView2.TrySuspendAsync();
        }
    }

    /// <summary>Reloads the page. Used after a theme is installed or edited, or the background is changed.</summary>
    public void ReloadWeb()
    {
        ApplyBackgroundMapping();
        _controller?.CoreWebView2.Reload();
    }

    /// <summary>
    /// Maps the folder holding the chosen video/interactive background to
    /// its own virtual host, so the web layer can load it as
    /// <c>https://verso.bg.local/name.ext</c> instead of a filesystem path
    /// script has no business seeing. A no-op for "image" and "none" — an
    /// image is applied straight to the real Windows wallpaper, and there is
    /// nothing to map for "none".
    /// </summary>
    private void ApplyBackgroundMapping()
    {
        if (_controller is null)
        {
            return;
        }

        var background = _settings.Current.Background;
        if (background.Kind is not ("video" or "interactive")
            || string.IsNullOrWhiteSpace(background.Source))
        {
            return;
        }

        var folder = Path.GetDirectoryName(background.Source);
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        _controller.CoreWebView2.SetVirtualHostNameToFolderMapping(
            ScriptBridge.BackgroundVirtualHost,
            folder,
            CoreWebView2HostResourceAccessKind.Allow);
    }

    /// <summary>
    /// Where the web layer lives.
    /// </summary>
    /// <remarks>
    /// An installed build has it beside the executable. Running from source that
    /// copy also exists, but preferring the repo's own folder means editing a
    /// theme and reloading works without a rebuild.
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _controller?.Close();
        _surface.Dispose();
    }
}
