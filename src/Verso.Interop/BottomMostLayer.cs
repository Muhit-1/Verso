using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Verso.Interop;

/// <summary>
/// Puts a window on the desktop without using <c>WorkerW</c>.
/// </summary>
/// <remarks>
/// <para>
/// The fallback for Windows builds where the <c>0x052C</c> desktop split no
/// longer happens — see <see cref="DesktopLayer.DetectLayout"/>. An ordinary
/// top-level window is made layered, given a colour key, and pinned to the
/// bottom of the Z-order. The result sits above the desktop and below every
/// application window.
/// </para>
/// <para>
/// What keeps the desktop usable is not this class but
/// <see cref="SurfaceRegion"/>, which reshapes the window to the rectangles the
/// desk actually draws in. The colour key set up here is a second line of
/// defence for the moment before the first region arrives.
/// </para>
/// <para>
/// It cannot be the primary mechanism: <c>LWA_COLORKEY</c> applies to
/// GDI-painted content, and WebView2 presents through DirectComposition, which
/// DWM composites straight to the screen. Measured — the window reports
/// <c>ex=0x8080080</c>, <c>LAYERED=True</c>, <c>key=0xFF00FF</c>,
/// <c>LWA_COLORKEY</c>, and renders fully opaque anyway. Passing
/// <c>--disable-gpu --disable-gpu-compositing</c> does not change it.
/// See docs/desktop-layer.md.
/// </para>
/// </remarks>
public static class BottomMostLayer
{
    /// <summary>
    /// The key colour, as Win32 expects it: <c>0x00BBGGRR</c>.
    /// </summary>
    /// <remarks>
    /// Magenta, the traditional choice, because nothing in a real design ever
    /// lands on exactly #FF00FF by accident.
    /// </remarks>
    public const uint KeyColorRef = 0x00FF00FF;

    /// <summary>The same colour as CSS. Must match <see cref="KeyColorRef"/> exactly.</summary>
    public const string KeyColorHtml = "#FF00FF";

    /// <summary>
    /// Makes a window a click-through-where-transparent desktop layer.
    /// </summary>
    /// <param name="hwnd">A shown top-level window.</param>
    /// <param name="clickThrough">
    /// When true the whole window ignores mouse input, key colour or not. Use it
    /// for a display that shows wallpaper but no widgets — there is nothing to
    /// click, and passing everything through keeps rubber-band selection and the
    /// desktop context menu working.
    /// </param>
    public static void Apply(nint hwnd, bool clickThrough = false)
    {
        var window = (HWND)hwnd;

        var ex = (WINDOW_EX_STYLE)PInvoke.GetWindowLongPtr(window, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);

        ex |= WINDOW_EX_STYLE.WS_EX_LAYERED      // colour key, and DWM composites us
            | WINDOW_EX_STYLE.WS_EX_TOOLWINDOW;  // no taskbar button, no alt-tab

        if (clickThrough)
        {
            ex |= WINDOW_EX_STYLE.WS_EX_TRANSPARENT;
        }
        else
        {
            ex &= ~WINDOW_EX_STYLE.WS_EX_TRANSPARENT;
        }

        PInvoke.SetWindowLongPtr(window, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, (nint)ex);
        PInvoke.SetLayeredWindowAttributes(window, new COLORREF(KeyColorRef), 0, LAYERED_WINDOW_ATTRIBUTES_FLAGS.LWA_COLORKEY);

        PinToBottom(hwnd);
    }

    /// <summary>
    /// Pushes the window to the bottom of the top-level Z-order, without
    /// activating it.
    /// </summary>
    /// <remarks>
    /// Has to be re-asserted. Activating another application, showing the
    /// desktop, or a display change can all raise us, and a wallpaper that
    /// occasionally appears on top of your editor is worse than no wallpaper.
    /// The desk re-asserts on a slow timer and on foreground changes rather than
    /// fighting per-frame.
    /// </remarks>
    public static void PinToBottom(nint hwnd)
    {
        PInvoke.SetWindowPos(
            (HWND)hwnd,
            (HWND)(nint)1, // HWND_BOTTOM
            0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE
            | SET_WINDOW_POS_FLAGS.SWP_NOSIZE
            | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
    }

    /// <summary>Whether the window is currently at the bottom of the Z-order.</summary>
    public static bool IsAtBottom(nint hwnd)
        => PInvoke.GetWindow((HWND)hwnd, GET_WINDOW_CMD.GW_HWNDNEXT).IsNull;

    /// <summary>
    /// Whether this window, or anything inside it, currently has the foreground.
    /// </summary>
    /// <remarks>
    /// The guard on re-pinning. Clicking a widget activates and raises the desk;
    /// pinning it back to the bottom while the user is still typing into it
    /// would drop it behind every other window mid-sentence. Walks to the root
    /// because the foreground window will be WebView2's child HWND, not ours.
    /// </remarks>
    public static bool IsForeground(nint hwnd)
    {
        var foreground = PInvoke.GetForegroundWindow();
        if (foreground.IsNull)
        {
            return false;
        }

        return (nint)PInvoke.GetAncestor(foreground, GET_ANCESTOR_FLAGS.GA_ROOT) == hwnd;
    }
}
