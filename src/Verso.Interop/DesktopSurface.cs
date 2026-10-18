using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Verso.Interop;

/// <summary>
/// A bare Win32 window for the desk to live in.
/// </summary>
/// <remarks>
/// <para>
/// Verso creates its own HWND rather than using a WPF <c>Window</c>, for one
/// specific reason: <b>WPF silently refuses <c>WS_EX_LAYERED</c></b>. Calling
/// <c>SetWindowLongPtr(GWL_EXSTYLE, … | WS_EX_LAYERED)</c> on a WPF window
/// returns success and sets no error, the flag never appears in the style, and
/// the subsequent <c>SetLayeredWindowAttributes</c> fails with
/// <c>ERROR_INVALID_PARAMETER</c>. WPF manages that bit itself through
/// <c>AllowsTransparency</c>, and <c>AllowsTransparency</c> uses per-pixel alpha
/// via <c>UpdateLayeredWindow</c>, which does not composite child HWNDs — so the
/// WebView2 inside would not render at all.
/// </para>
/// <para>
/// The way out is to set the extended styles at creation time on a window we
/// own, then host WebView2 in it through the Win32 hosting API
/// (<c>CreateCoreWebView2ControllerAsync(hwnd)</c>) instead of the WPF control.
/// The colour key then applies to the whole composited window, WebView2
/// included.
/// </para>
/// <para>
/// The window lives on the UI thread and relies on the WPF dispatcher already
/// pumping messages for that thread, so it needs no message loop of its own.
/// </para>
/// </remarks>
public sealed unsafe class DesktopSurface : IDeskSurface
{
    internal const string ClassName = "VersoDesktopSurface";

    /// <summary>
    /// Held in a static field for the lifetime of the process. If this is
    /// collected, Windows calls into freed memory the next time the window gets
    /// a message.
    /// </summary>
    private static WNDPROC? _wndProc;

    private static ushort _classAtom;

    private HWND _hwnd;
    private bool _disposed;

    /// <summary>Creates and shows the surface, without activating it.</summary>
    /// <param name="x">Left edge, in physical pixels.</param>
    /// <param name="y">Top edge, in physical pixels.</param>
    /// <param name="width">Width, in physical pixels.</param>
    /// <param name="height">Height, in physical pixels.</param>
    /// <param name="clickThrough">
    /// When true the whole window ignores mouse input. Use it for a display that
    /// shows scenery but no widgets — there is nothing to click, and passing
    /// everything through keeps rubber-band selection and the desktop context
    /// menu working across the whole screen.
    /// </param>
    /// <remarks>
    /// <para>
    /// Deliberately <b>not</b> <c>WS_EX_NOACTIVATE</c>. That flag is the obvious
    /// choice for scenery and it is wrong here: a window that never activates
    /// never takes keyboard focus, so the task field cannot be typed into. The
    /// desk is meant to be written in, not just looked at.
    /// </para>
    /// <para>
    /// The cost is that clicking the desk raises it. That is handled by only
    /// re-pinning to the bottom while the desk is not the foreground window —
    /// see <see cref="BottomMostLayer.IsForeground"/> — so it drops back the
    /// moment you click away, and does not get yanked out from under you
    /// mid-sentence.
    /// </para>
    /// </remarks>
    public DesktopSurface(int x, int y, int width, int height, bool clickThrough = false)
    {
        EnsureClassRegistered();

        var ex = WINDOW_EX_STYLE.WS_EX_LAYERED       // DWM composites us as one surface
               | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW;   // no taskbar button, no alt-tab

        if (clickThrough)
        {
            ex |= WINDOW_EX_STYLE.WS_EX_TRANSPARENT;
        }

        // CsWin32 offers a friendly overload taking plain strings here.
        _hwnd = PInvoke.CreateWindowEx(
            ex,
            ClassName,
            "Verso",
            WINDOW_STYLE.WS_POPUP,
            x, y, width, height,
            HWND.Null, null, null, null);

        if (_hwnd.IsNull)
        {
            throw new InvalidOperationException(
                $"Could not create the Verso desktop surface (Win32 error {Marshal.GetLastWin32Error()}).");
        }

        // The colour key is not what makes the desk transparent — the window
        // region is (see SurfaceRegion). This is set anyway as a second line of
        // defence for the instant before the first region arrives, and because
        // WS_EX_LAYERED without any attributes set leaves a window invisible.
        PInvoke.SetLayeredWindowAttributes(
            _hwnd,
            new COLORREF(BottomMostLayer.KeyColorRef),
            0,
            LAYERED_WINDOW_ATTRIBUTES_FLAGS.LWA_COLORKEY);

        PInvoke.ShowWindow(_hwnd, SHOW_WINDOW_CMD.SW_SHOWNA); // show without activating
        BottomMostLayer.PinToBottom(_hwnd);
    }

    /// <summary>The window handle. Pass this to WebView2's Win32 hosting API.</summary>
    public nint Handle => _hwnd;

    /// <summary>Pushes the surface back to the bottom of the Z-order.</summary>
    public void PinToBottom()
    {
        if (!_disposed)
        {
            BottomMostLayer.PinToBottom(_hwnd);
        }
    }

    /// <summary>Shows or hides the surface, without ever activating it.</summary>
    public void SetVisible(bool visible)
    {
        if (_disposed)
        {
            return;
        }

        PInvoke.ShowWindow(_hwnd, visible ? SHOW_WINDOW_CMD.SW_SHOWNA : SHOW_WINDOW_CMD.SW_HIDE);
        if (visible)
        {
            BottomMostLayer.PinToBottom(_hwnd);
        }
    }

    /// <summary>The client area, in physical pixels.</summary>
    public (int Width, int Height) ClientSize
    {
        get
        {
            PInvoke.GetClientRect(_hwnd, out var rect);
            return (rect.right - rect.left, rect.bottom - rect.top);
        }
    }

    internal static void EnsureClassRegistered()
    {
        if (_classAtom != 0)
        {
            return;
        }

        // DefWindowProc for everything. The surface has no behaviour of its
        // own — it is a container for a WebView2, which handles its own input.
        _wndProc = static (hwnd, msg, wParam, lParam) =>
            PInvoke.DefWindowProc(hwnd, msg, wParam, lParam);

        fixed (char* className = ClassName)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = _wndProc,
                hInstance = (HINSTANCE)(nint)PInvoke.GetModuleHandle((PCWSTR)null),
                lpszClassName = new PCWSTR(className),
                // No background brush: WebView2 paints every pixel, and letting
                // Windows erase to a system colour first causes a visible flash.
                hbrBackground = default
            };

            _classAtom = PInvoke.RegisterClassEx(in wc);
        }

        if (_classAtom == 0)
        {
            throw new InvalidOperationException(
                $"Could not register the Verso window class (Win32 error {Marshal.GetLastWin32Error()}).");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (!_hwnd.IsNull)
        {
            PInvoke.DestroyWindow(_hwnd);
            _hwnd = HWND.Null;
        }
    }
}
