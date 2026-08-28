using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Verso.Interop;

/// <summary>
/// The dashboard window has no OS title bar — see docs/dashboard-theme-spec.md
/// and <c>web/dashboard/js/titlebar.js</c> — so dragging it has to be done by
/// hand. This is the standard technique for a borderless window with its own
/// drawn caption: release the mouse capture the click already took, then tell
/// Windows the click landed on a caption, exactly as if the (nonexistent)
/// native title bar had been dragged. Windows does the actual move.
/// </summary>
public static class TitleBarDrag
{
    public static void Begin(nint hwnd)
    {
        PInvoke.ReleaseCapture();
        PInvoke.SendMessage((HWND)hwnd, PInvoke.WM_NCLBUTTONDOWN, (WPARAM)PInvoke.HTCAPTION, 0);
    }
}
