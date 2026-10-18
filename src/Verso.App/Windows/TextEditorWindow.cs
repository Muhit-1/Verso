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

namespace Verso.App.Windows;

/// <summary>
/// The pop-up editor: a small borderless top-most window with a WebView2 (contenteditable + toolbar), placed exactly
/// over the note or box name the user clicked on the desk.
/// </summary>
/// <remarks>
/// <para>
/// Why a real window: the desk page lives behind the desktop icons, where only the mouse is mirrored in; keyboard
/// input goes to whatever window is in front. A real window gets real keyboard focus, IME and keyboard layouts for
/// free — which is exactly what translating raw key events could not do.
/// </para>
/// <para>
/// Starting a WebView2 renderer takes about two seconds on this machine, which is too long to wait between clicking a
/// note and typing. So the first editor stays alive off-screen after it is closed and is reused for the next one; it
/// is disposed after <see cref="IdleLifetime"/> without use, so it costs memory only around the time someone is
/// writing. Until its page is ready the window stays off-screen, so nobody sees or types into a half-loaded editor.
/// </para>
/// <para>
/// Text is autosaved a moment after the last keystroke (<c>onChange</c>) and committed once more when the editing
/// session ends: Esc, the Done button, Enter on a single line, or clicking anywhere else.
/// </para>
/// </remarks>
internal sealed class TextEditorWindow : Window
{
    private const string VirtualHost = "verso.local";
    private const int MinWidth_ = 240;
    private const int MinHeight_ = 150;
    private const int Parked = -32000;

    /// <summary>How long a closed editor is kept ready before it is disposed.</summary>
    private static readonly TimeSpan IdleLifetime = TimeSpan.FromMinutes(3);

    private static Task<CoreWebView2Environment>? _environment;

    private readonly WebView2 _browser = new();
    private readonly DispatcherTimer _idle = new() { Interval = IdleLifetime };

    // the editing session in progress, if any
    private EditRequest? _request;
    private Action<string>? _onChange;
    private Action<string>? _onCommit;
    private Action? _onEnded;
    private DateTime _revealedAt;
    private IntPtr _previousForeground;
    private bool _pageReady;
    private bool _ending;
    private (EditRequest, Action<string>, Action<string>, Action)? _pending;

    /// <summary>Creates the shared WebView2 environment ahead of time so the first editor opens quickly.</summary>
    public static void WarmUp() => _environment ??= CoreWebView2Environment.CreateAsync(null, VersoPaths.WebViewUserData);

    /// <summary>Raised when the standing editor has been unused for a while and is going away.</summary>
    public event Action? Retired;

    public TextEditorWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        AllowsTransparency = false;
        Background = new SolidColorBrush(Color.FromRgb(0x2b, 0x2b, 0x2b));
        WindowStartupLocation = WindowStartupLocation.Manual;
        Title = "Verso editor";
        Content = _browser;

        // built off-screen; moved over the element only when there is something to edit
        Left = Parked;
        Top = Parked;
        Width = MinWidth_;
        Height = MinHeight_;

        Deactivated += OnDeactivated;
        Loaded += async (_, _) => await InitializeAsync();
        Closed += (_, _) =>
        {
            _idle.Stop();
            _browser.Dispose();
        };

        _idle.Tick += (_, _) =>
        {
            _idle.Stop();
            if (_request is null)
            {
                Retired?.Invoke();
                Close();
            }
        };
    }

    /// <summary>
    /// Starts editing. Shows the editor over the element as soon as its page is ready (at once when it was used
    /// before).
    /// </summary>
    /// <param name="onChange">Autosave while typing.</param>
    /// <param name="onCommit">The final value, once, when the session ends.</param>
    /// <param name="onEnded">The session is over and the editor is out of sight.</param>
    public void Begin(EditRequest request, Action<string> onChange, Action<string> onCommit, Action onEnded)
    {
        if (_request is not null)
        {
            // already editing something else: finish that one first, then start this one
            _pending = (request, onChange, onCommit, onEnded);
            CommitAndEnd();
            return;
        }

        _idle.Stop();
        _request = request;
        _onChange = onChange;
        _onCommit = onCommit;
        _onEnded = onEnded;
        _ending = false;

        if (_pageReady)
        {
            Present();
        }
    }

    /// <summary>Ends the current session with whatever is typed so far.</summary>
    public async void CommitAndEnd()
    {
        if (_request is null || _ending)
        {
            return;
        }

        _ending = true;

        try
        {
            if (_browser.CoreWebView2 is { } core)
            {
                var json = await core.ExecuteScriptAsync("window.__editorValue&&window.__editorValue()");
                var value = JsonSerializer.Deserialize<string>(json);
                if (value is not null)
                {
                    _onCommit?.Invoke(value);
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException or JsonException or ObjectDisposedException)
        {
            // The page is already gone; the autosave has the last text.
        }

        End();
    }

    private async Task InitializeAsync()
    {
        WarmUp();
        await _browser.EnsureCoreWebView2Async(await _environment!);

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
        core.WebMessageReceived += OnMessage;
        core.Navigate($"https://{VirtualHost}/editor/index.html");
    }

    private void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
            var value = root.TryGetProperty("value", out var v) ? v.GetString() : null;

            switch (type)
            {
                case "ready":
                    _pageReady = true;
                    if (_request is not null)
                    {
                        Present();
                    }

                    break;

                case "change" when value is not null && _request is not null && !_ending:
                    _onChange?.Invoke(value);
                    break;

                case "done" when value is not null && _request is not null && !_ending:
                    _ending = true;
                    _onCommit?.Invoke(value);
                    End();
                    break;
            }
        }
        catch (JsonException)
        {
            // The page can put anything on this channel. Ignore it.
        }
    }

    // ---- showing and hiding ----------------------------------------------------

    /// <summary>Fills the page with the text and moves the editor exactly over the element (physical pixels).</summary>
    private void Present()
    {
        if (_request is not { } r)
        {
            return;
        }

        _browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
        {
            type = "init",
            kind = r.Kind,
            value = r.Value,
            color = r.Color,
            single = r.Single
        }));

        var hwnd = new WindowInteropHelper(this).Handle;
        NativeEditor.SetWindowPos(hwnd, NativeEditor.HwndTopmost,
            r.X, r.Y, Math.Max(r.Width, MinWidth_), Math.Max(r.Height, MinHeight_), NativeEditor.NoActivate | NativeEditor.ShowWindow);
        _revealedAt = DateTime.UtcNow;

        // remember who had the keyboard, to hand it back when the session ends
        var before = NativeEditor.GetForegroundWindow();
        _previousForeground = before == hwnd ? IntPtr.Zero : before;
        TakeFocus(hwnd);
    }

    /// <summary>The session is over: put the editor back off-screen, ready for the next one.</summary>
    private void End()
    {
        var ended = _onEnded;
        _request = null;
        _onChange = null;
        _onCommit = null;
        _onEnded = null;
        _ending = false;

        var hwnd = new WindowInteropHelper(this).Handle;
        var hadFocus = NativeEditor.GetForegroundWindow() == hwnd;
        var previous = _previousForeground;
        _previousForeground = IntPtr.Zero;

        // hidden, not just moved away, and without NOACTIVATE: hiding the active window passes the keyboard focus to
        // the next window; an off-screen one would keep it
        NativeEditor.SetWindowPos(hwnd, NativeEditor.HwndTopmost, Parked, Parked, MinWidth_, MinHeight_, NativeEditor.HideWindow);

        // Hiding does not pass the keyboard on by itself. If the editor still had it (Esc, Done, Enter), give it back
        // to the window that had it before; if the user clicked elsewhere, that window already has it.
        if (hadFocus && previous != IntPtr.Zero && NativeEditor.IsWindow(previous) && NativeEditor.IsWindowVisible(previous))
        {
            Activate(previous);
        }

        _idle.Stop();
        _idle.Start();

        ended?.Invoke();

        if (_pending is { } next)
        {
            _pending = null;
            Begin(next.Item1, next.Item2, next.Item3, next.Item4);
        }
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        // Clicking anywhere else commits. Not in the first moments, while focus is still being settled.
        if (_request is not null && (DateTime.UtcNow - _revealedAt).TotalMilliseconds > 400)
        {
            CommitAndEnd();
        }
    }

    /// <summary>
    /// The click that opened the editor reached us through the mirrored mouse, not as real input to this process, so
    /// Windows would normally refuse to move the keyboard focus here. Attaching to the foreground thread's input for a
    /// moment is the standard way to get it.
    /// </summary>
    private void TakeFocus(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        Activate(hwnd);
        _browser.Focus();
    }

    private static void Activate(IntPtr target)
    {
        var foreground = NativeEditor.GetForegroundWindow();
        var foregroundThread = foreground == IntPtr.Zero ? 0 : NativeEditor.GetWindowThreadProcessId(foreground, out _);
        var thisThread = NativeEditor.GetCurrentThreadId();
        var attached = foregroundThread != 0 && foregroundThread != thisThread
                       && NativeEditor.AttachThreadInput(thisThread, foregroundThread, true);
        try
        {
            NativeEditor.BringWindowToTop(target);
            NativeEditor.SetForegroundWindow(target);
        }
        finally
        {
            if (attached)
            {
                NativeEditor.AttachThreadInput(thisThread, foregroundThread, false);
            }
        }
    }

    private static class NativeEditor
    {
        public static readonly IntPtr HwndTopmost = new(-1);
        public const uint NoActivate = 0x0010;
        public const uint ShowWindow = 0x0040;
        public const uint HideWindow = 0x0080;

        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
        [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    }
}
