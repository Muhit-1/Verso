using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;

namespace Verso.Interop;

/// <summary>
/// Restricts a window to the rectangles it actually draws in.
/// </summary>
/// <remarks>
/// <para>
/// This is how Verso leaves the desktop usable. Pixels outside a window's
/// region are not part of the window at all: they are not painted, and mouse
/// input at those coordinates goes to whatever is underneath. So the desktop
/// icons stay visible <em>and</em> stay clickable, rubber-band selection still
/// works, and the desktop context menu still opens — everywhere the desk is not.
/// </para>
/// <para>
/// A region is the mechanism that works here, and the two more obvious ones do
/// not:
/// </para>
/// <list type="bullet">
///   <item><b>Colour key</b> (<c>SetLayeredWindowAttributes</c> +
///   <c>LWA_COLORKEY</c>). Applies to GDI-painted content. WebView2 presents
///   through DirectComposition, which DWM composites straight to the screen, so
///   the key never sees it: the window correctly reports <c>LAYERED=True</c>
///   with the right key and renders fully opaque anyway. Passing
///   <c>--disable-gpu --disable-gpu-compositing</c> does not change this.</item>
///   <item><b>Per-pixel alpha</b> (<c>UpdateLayeredWindow</c>). Does not
///   composite child HWNDs at all, so the WebView2 would not render.</item>
/// </list>
/// <para>
/// The cost is that a region is geometric, so the interactive shape is built
/// from rectangles. That suits this design — the panels <em>are</em> rectangles —
/// and rounded corners are supported per rectangle.
/// </para>
/// </remarks>
public static class SurfaceRegion
{
    /// <summary>One rectangle of the window that should exist, in physical pixels.</summary>
    public readonly record struct Panel(int X, int Y, int Width, int Height, int CornerRadius);

    /// <summary>
    /// Reshapes <paramref name="hwnd"/> to the union of <paramref name="panels"/>.
    /// </summary>
    /// <remarks>
    /// Passing an empty list makes the window vanish entirely, which is the
    /// correct behaviour while the page has not yet reported any geometry —
    /// better than a full-screen rectangle appearing for a frame.
    /// </remarks>
    public static void Apply(nint hwnd, IReadOnlyList<Panel> panels)
    {
        // An empty region rather than a null one: SetWindowRgn(null) means
        // "the whole window", which is the opposite of what an empty list means.
        var combined = PInvoke.CreateRectRgn(0, 0, 0, 0);

        foreach (var panel in panels)
        {
            if (panel.Width <= 0 || panel.Height <= 0)
            {
                continue;
            }

            var right = panel.X + panel.Width;
            var bottom = panel.Y + panel.Height;

            var piece = panel.CornerRadius > 0
                // +1: CreateRoundRectRgn's ellipse extents are exclusive, and
                // without it the corners come out a pixel tighter than the CSS.
                ? PInvoke.CreateRoundRectRgn(panel.X, panel.Y, right + 1, bottom + 1,
                                             panel.CornerRadius * 2, panel.CornerRadius * 2)
                : PInvoke.CreateRectRgn(panel.X, panel.Y, right, bottom);

            PInvoke.CombineRgn(combined, combined, piece, RGN_COMBINE_MODE.RGN_OR);
            PInvoke.DeleteObject((HGDIOBJ)(nint)piece);
        }

        // Ownership of the region passes to the window on success, so it must
        // not be deleted here. On failure it would leak, hence the delete.
        if (PInvoke.SetWindowRgn((HWND)hwnd, combined, bRedraw: true) == 0)
        {
            PInvoke.DeleteObject((HGDIOBJ)(nint)combined);
        }
    }

    /// <summary>Restores the window to its full rectangle.</summary>
    public static void Clear(nint hwnd) => PInvoke.SetWindowRgn((HWND)hwnd, default, bRedraw: true);
}
