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
