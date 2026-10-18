using System.Drawing;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace Verso.Interop;

/// <summary>
/// Real per-monitor enumeration, in physical pixels.
/// </summary>
/// <remarks>
/// Replaces the Phase 1 stand-in — one <see cref="System.Windows.SystemParameters"/>
/// rectangle spanning the whole virtual screen, which put every monitor's
/// share of the desk in one combined canvas and could not tell a bezel gap
/// from a widget's own position. <c>rcMonitor</c> from <c>GetMonitorInfoW</c>
/// is already in physical pixels: the app manifest declares PerMonitorV2 DPI
/// awareness (Phase 1), so no additional per-monitor DPI math is needed to
/// place a window correctly — WebView2 scales its own content per monitor
/// once it is hosted there.
/// </remarks>
public static class Monitors
{
    /// <summary>
    /// The part of a monitor not covered by the taskbar (<c>rcWork</c>), in physical pixels. Falls back
    /// to the whole monitor if it cannot be found. The Outpost desk keeps its elements inside this.
    /// </summary>
    public static unsafe Rectangle WorkAreaOf(Rectangle monitorBounds)
    {
        var result = monitorBounds;

        PInvoke.EnumDisplayMonitors(default, null, Callback, default);

        return result;

        BOOL Callback(HMONITOR monitor, HDC hdc, RECT* rect, LPARAM data)
        {
            var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
            if (PInvoke.GetMonitorInfo(monitor, ref info))
            {
                var m = info.rcMonitor;
                if (m.left == monitorBounds.Left && m.top == monitorBounds.Top
                    && m.right - m.left == monitorBounds.Width && m.bottom - m.top == monitorBounds.Height)
                {
                    var w = info.rcWork;
                    result = new Rectangle(w.left, w.top, w.right - w.left, w.bottom - w.top);
                }
            }

            return true;
        }
    }

    public static unsafe IReadOnlyList<Rectangle> All()
    {
        var bounds = new List<Rectangle>();

        PInvoke.EnumDisplayMonitors(default, null, Callback, default);

        return bounds;

        BOOL Callback(HMONITOR monitor, HDC hdc, RECT* rect, LPARAM data)
        {
            var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
            if (PInvoke.GetMonitorInfo(monitor, ref info))
            {
                var r = info.rcMonitor;
                bounds.Add(new Rectangle(r.left, r.top, r.right - r.left, r.bottom - r.top));
            }

            return true;
        }
    }
}
