using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Verso.Interop;

/// <summary>
/// Hides and restores the taskbar and the desktop icons — the "clean
/// desktop" toggles from the tray.
/// </summary>
/// <remarks>
/// Independent of <see cref="DesktopLayer"/>: neither of these touches
/// WorkerW or where Verso itself sits, so they work the same whether or not
/// the WorkerW technique does. See docs/plan.md, Phase 4.
/// </remarks>
public static class DesktopChrome
{
    /// <summary>Shows or hides the taskbar, including a secondary one on every other monitor.</summary>
    public static void SetTaskbarVisible(bool visible)
    {
        var cmd = visible ? SHOW_WINDOW_CMD.SW_SHOW : SHOW_WINDOW_CMD.SW_HIDE;

        var primary = PInvoke.FindWindow("Shell_TrayWnd", null);
        if (!primary.IsNull)
        {
            PInvoke.ShowWindow(primary, cmd);
        }

        for (var secondary = PInvoke.FindWindowEx(HWND.Null, HWND.Null, "Shell_SecondaryTrayWnd", null);
             !secondary.IsNull;
             secondary = PInvoke.FindWindowEx(HWND.Null, secondary, "Shell_SecondaryTrayWnd", null))
        {
            PInvoke.ShowWindow(secondary, cmd);
        }
    }

    /// <summary>
    /// Shows or hides the desktop icons.
    /// </summary>
    /// <remarks>
    /// Hides the <c>SysListView32</c> inside <c>SHELLDLL_DefView</c> — the
    /// same icon layer <see cref="DesktopLayer"/> looks for to stay behind —
    /// rather than the per-user registry setting Explorer's own "Show desktop
    /// icons" menu item flips. A hidden window comes back the instant this is
    /// called again; the registry route needs Explorer told to refresh and
    /// survives a crash in the wrong state.
    /// </remarks>
    public static void SetDesktopIconsVisible(bool visible)
    {
        var defView = DesktopLayer.FindDefView();
        if (defView.IsNull)
        {
            return;
        }

        var listView = PInvoke.FindWindowEx(defView, HWND.Null, "SysListView32", null);
        if (!listView.IsNull)
        {
            PInvoke.ShowWindow(listView, visible ? SHOW_WINDOW_CMD.SW_SHOW : SHOW_WINDOW_CMD.SW_HIDE);
        }
    }
}
