using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Verso.App.Hosting;
using Verso.Core;
using Verso.Interop;

namespace Verso.App.Windows;

/// <summary>
/// The "Add apps" picker: a small borderless top-most window with a WebView2 page (<c>web/picker</c>) that lists the
/// installed apps, with search, tick boxes and a way to browse for a file instead.
/// </summary>
/// <remarks>
/// Same technique as <see cref="TextEditorWindow"/> and for the same reasons: a real window takes real typing for the
/// search box, and the page is kept (hidden) after it closes so the next open is instant instead of waiting ~2 s for a
/// new WebView2. It is disposed after <see cref="IdleLifetime"/> unused. Opened next to the box whose <c>+</c> was
/// clicked (desk) or centred (dashboard). Clicking anywhere else cancels.
/// </remarks>
internal sealed class AppPickerWindow : Window
{
    private const string VirtualHost = "verso.example";
    private const int WidthDip = 560;
    private const int HeightDip = 640;
    private const int Parked = -32000;
    private static readonly TimeSpan IdleLifetime = TimeSpan.FromMinutes(3);

    private readonly WebView2 _browser = new();
    private readonly DispatcherTimer _idle = new() { Interval = IdleLifetime };
    private readonly PickerBridge _bridge;
    private readonly LauncherLibrary _library;

    private string? _boxId;
    private Rectangle? _anchor;
    private bool _pageReady;
    private bool _modal;
    private DateTime _shownAt;
    private IntPtr _previousForeground;

    /// <summary>Raised when the standing picker has been unused for a while and is going away.</summary>
    public event Action? Retired;

    public AppPickerWindow(LauncherLibrary library)
    {
        _library = library;
        _bridge = new PickerBridge(library, End, Browse);

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        AllowsTransparency = false;
        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2b, 0x2b, 0x2b));
        WindowStartupLocation = WindowStartupLocation.Manual;
        Title = "Verso app picker";
        Content = _browser;
        Left = Parked;
        Top = Parked;
        Width = WidthDip;
        Height = HeightDip;

        Loaded += async (_, _) => await InitializeAsync();
        Deactivated += OnDeactivated;
        Closed += (_, _) =>
        {
            _idle.Stop();
            _browser.Dispose();
        };

        _idle.Tick += (_, _) =>
        {
            _idle.Stop();
            if (_boxId is null)
            {
                Retired?.Invoke();
                Close();
            }
        };
    }

    /// <summary>Shows the picker for a box. <paramref name="anchor"/> = the box on screen (physical pixels), or null to centre it.</summary>
    public void Begin(string boxId, Rectangle? anchor)
    {
        _idle.Stop();
        _boxId = boxId;
        _anchor = anchor;
        _bridge.Reload();

        if (_pageReady)
        {
            Present();
        }
    }

    private async Task InitializeAsync()
    {
        TextEditorWindow.WarmUp();
        await _browser.EnsureCoreWebView2Async(await TextEditorWindow.SharedEnvironment!);

        var core = _browser.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.IsZoomControlEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.IsGeneralAutofillEnabled = false;
        core.Settings.IsPasswordAutosaveEnabled = false;
#if DEBUG
        core.Settings.AreDevToolsEnabled = true;
#else
        core.Settings.AreDevToolsEnabled = false;
#endif
        _browser.DefaultBackgroundColor = System.Drawing.Color.FromArgb(0x2b, 0x2b, 0x2b);

        core.SetVirtualHostNameToFolderMapping(VirtualHost, VersoPaths.ResolveWebRoot(), CoreWebView2HostResourceAccessKind.Allow);
        Hosting.WebViewGuard.Apply(core, "picker");
        core.AddHostObjectToScript("VersoPicker", _bridge);
        core.WebMessageReceived += (_, e) =>
        {
            try
            {
                using var doc = JsonDocument.Parse(e.WebMessageAsJson);
                if (doc.RootElement.TryGetProperty("type", out var t) && t.GetString() == "ready")
                {
                    _pageReady = true;
                    if (_boxId is not null)
                    {
                        Present();
                    }
                }
            }
            catch (JsonException)
            {
                // the page can put anything on this channel
            }
        };
        core.Navigate($"https://{VirtualHost}/picker/index.html");
    }

    // ---- showing and hiding ----------------------------------------------------

    private void Present()
    {
        if (_boxId is null)
        {
            return;
        }

        _browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { type = "init", boxId = _boxId }));

        var hwnd = new WindowInteropHelper(this).Handle;
        var scale = Native.GetDpiForWindow(hwnd) / 96.0;
        var width = (int)Math.Round(WidthDip * scale);
        var height = (int)Math.Round(HeightDip * scale);

        // the monitor the box (or the cursor) is on
        Native.GetCursorPos(out var cursor);
        var probe = _anchor is { } a ? new System.Drawing.Point(a.X + a.Width / 2, a.Y + a.Height / 2) : new System.Drawing.Point(cursor.X, cursor.Y);
        var monitor = Monitors.All().FirstOrDefault(m => m.Contains(probe));
        var work = monitor.IsEmpty ? new Rectangle(0, 0, 1920, 1040) : Monitors.WorkAreaOf(monitor);

        int x;
        int y;
        if (_anchor is { } box)
        {
            // beside the box: on its left (the boxes live at the right edge), else on its right
            x = box.Left - width - 16;
            if (x < work.Left)
            {
                x = box.Right + 16;
            }

            y = box.Top;
        }
        else
        {
            x = work.Left + (work.Width - width) / 2;
            y = work.Top + (work.Height - height) / 2;
        }

        x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - width));
        y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - height));

        Native.SetWindowPos(hwnd, Native.HwndTopmost, x, y, width, height, Native.NoActivate | Native.ShowWindow);
        _shownAt = DateTime.UtcNow;

        var before = Native.GetForegroundWindow();
        _previousForeground = before == hwnd ? IntPtr.Zero : before;
        Activate(hwnd);
        _browser.Focus();
    }

    /// <summary>Ends the session: hide the window (it stays alive, ready for the next time) and hand the keyboard back.</summary>
    private void End()
    {
        if (_boxId is null)
        {
            return;
        }

        _boxId = null;
        var hwnd = new WindowInteropHelper(this).Handle;
        var hadFocus = Native.GetForegroundWindow() == hwnd;
        var previous = _previousForeground;
        _previousForeground = IntPtr.Zero;

        Native.SetWindowPos(hwnd, Native.HwndTopmost, Parked, Parked, 100, 100, Native.HideWindow);

        if (hadFocus && previous != IntPtr.Zero && Native.IsWindow(previous) && Native.IsWindowVisible(previous))
        {
            Activate(previous);
        }

        _idle.Stop();
        _idle.Start();
    }

    private void Browse(string boxId)
    {
        _modal = true;
        try
        {
            _library.PickAndAdd(boxId, this);
        }
        finally
        {
            _modal = false;
        }

        End();
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        // clicking anywhere else cancels; not while the file dialog (owned by this window) is up, nor in the first moments
        if (_boxId is not null && !_modal && (DateTime.UtcNow - _shownAt).TotalMilliseconds > 400)
        {
            End();
        }
    }

    private static void Activate(IntPtr target)
    {
        var foreground = Native.GetForegroundWindow();
        var foregroundThread = foreground == IntPtr.Zero ? 0 : Native.GetWindowThreadProcessId(foreground, out _);
        var thisThread = Native.GetCurrentThreadId();
        var attached = foregroundThread != 0 && foregroundThread != thisThread
                       && Native.AttachThreadInput(thisThread, foregroundThread, true);
        try
        {
            Native.BringWindowToTop(target);
            Native.SetForegroundWindow(target);
        }
        finally
        {
            if (attached)
            {
                Native.AttachThreadInput(thisThread, foregroundThread, false);
            }
        }
    }

    private static class Native
    {
        public static readonly IntPtr HwndTopmost = new(-1);
        public const uint NoActivate = 0x0010;
        public const uint ShowWindow = 0x0040;
        public const uint HideWindow = 0x0080;

        [StructLayout(LayoutKind.Sequential)] public struct Pt { public int X, Y; }

        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
        [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out Pt point);
        [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hWnd);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    }
}
