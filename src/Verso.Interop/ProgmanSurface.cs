using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Verso.Interop;

/// <summary>
/// Helpers for the layer behind the desktop icons on current Windows 11.
/// </summary>
public static class ProgmanLayer
{
    /// <summary>Undocumented. Asks Explorer to give Progman its extra WorkerW child.</summary>
    private const uint WM_SPAWN_WORKER = 0x052C;

    /// <summary>
    /// Sends the split message and waits for Progman to grow its <c>WorkerW</c>
    /// child. Safe to call repeatedly. Returns whether the layer can be used.
    /// </summary>
    /// <remarks>
    /// Required, not optional: a freshly started Explorer has no such child,
    /// and a <see cref="ProgmanSurface"/> placed without it is invisible
    /// (measured — see CLAUDE.md, "Step 1b result"). Explorer creates it in
    /// well under a frame once asked.
    /// </remarks>
    public static async Task<bool> PrepareAsync(int timeoutMs = 3000)
    {
        var progman = PInvoke.FindWindow("Progman", null);
        if (progman.IsNull)
        {
            return false;
        }

        SendSplit(progman);

        var waited = 0;
        while (DesktopLayer.FindProgmanChildWorkerW(progman).IsNull)
        {
            if (waited >= timeoutMs)
            {
                return false;
            }

            await Task.Delay(50);
            waited += 50;
        }

        return !FindDefView(progman).IsNull;
    }

    /// <summary>Synchronous variant for startup detection. Polls briefly.</summary>
    internal static bool WaitForWorkerWChild(HWND progman, int timeoutMs)
    {
        var waited = 0;
        while (DesktopLayer.FindProgmanChildWorkerW(progman).IsNull)
        {
            if (waited >= timeoutMs)
            {
                return false;
            }

            Thread.Sleep(25);
            waited += 25;
        }

        return true;
    }

    internal static void SendSplit(HWND progman)
        => PInvoke.SendMessageTimeout(
            progman, WM_SPAWN_WORKER,
            (WPARAM)(nuint)0xD, (LPARAM)(nint)0x1,
            SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_NORMAL, uTimeout: 1000);

    internal static HWND FindDefView(HWND progman)
        => PInvoke.FindWindowEx(progman, HWND.Null, "SHELLDLL_DefView", null);
}

/// <summary>
/// A window that is genuinely behind the desktop icons: a layered child of
/// <c>Progman</c>, placed directly behind <c>SHELLDLL_DefView</c>.
/// </summary>
/// <remarks>
/// <para>
/// Found by experiment, not documentation — <c>tools/Verso.Spike.Layers</c>
/// painted a test window seven different ways and checked the composed screen.
/// Only this one was visible with the icons and taskbar still drawn on top:
/// </para>
/// <list type="bullet">
///   <item>a child of Progman (not top-level, not inside its WorkerW);</item>
///   <item><c>WS_EX_LAYERED</c> with <c>LWA_ALPHA</c> 255 — without it the
///   window reports success and draws nothing;</item>
///   <item>inserted directly behind <c>SHELLDLL_DefView</c>, so the icon layer
///   stays in front and the shell's own <c>WorkerW</c> stays behind;</item>
///   <item>and Progman must already have its <c>WorkerW</c> child, which only
///   exists after <see cref="ProgmanLayer.PrepareAsync"/>.</item>
/// </list>
/// <para>
/// Unlike <see cref="DesktopSurface"/> this is never raised above other
/// windows, never covers the icons, and needs no window region. The icon layer
/// sits in front of it and takes all input, so a wallpaper hosted here is
/// look-only — which is exactly what a wallpaper is.
/// </para>
/// <para>
/// It is a child of Explorer's window, so it dies with Explorer. The owner
/// (see <see cref="ShellRestartWatcher"/>) has to build a new one when the
/// shell comes back.
/// </para>
/// </remarks>
public sealed unsafe class ProgmanSurface : IDeskSurface
{
    private HWND _hwnd;
    private bool _disposed;

    /// <param name="x">Left edge in physical virtual-screen pixels.</param>
    /// <param name="y">Top edge in physical virtual-screen pixels.</param>
    /// <param name="width">Width in physical pixels.</param>
    /// <param name="height">Height in physical pixels.</param>
    /// <exception cref="InvalidOperationException">
    /// The layer is not available. Call <see cref="ProgmanLayer.PrepareAsync"/>
    /// first and only construct this when it returned true.
    /// </exception>
    public ProgmanSurface(int x, int y, int width, int height)
    {
        DesktopSurface.EnsureClassRegistered();

        var progman = PInvoke.FindWindow("Progman", null);
        if (progman.IsNull)
        {
            throw new InvalidOperationException("Explorer is not running (no Progman).");
        }

        if (ProgmanLayer.FindDefView(progman).IsNull)
        {
            throw new InvalidOperationException("Progman has no SHELLDLL_DefView; the desktop is not ready.");
        }

        if (DesktopLayer.FindProgmanChildWorkerW(progman).IsNull)
        {
            throw new InvalidOperationException(
                "Progman has no WorkerW child. A window placed now would be invisible. Call ProgmanLayer.PrepareAsync first.");
        }

        // A child's coordinates are relative to Progman, which starts at the
        // top-left of the virtual screen — not necessarily (0, 0) with
        // monitors arranged to the left of or above the primary one.
        PInvoke.GetWindowRect(progman, out var parent);

        _hwnd = PInvoke.CreateWindowEx(
            WINDOW_EX_STYLE.WS_EX_LAYERED,
            DesktopSurface.ClassName,
            "Verso wallpaper",
            WINDOW_STYLE.WS_CHILD | WINDOW_STYLE.WS_VISIBLE | WINDOW_STYLE.WS_CLIPSIBLINGS,
            x - parent.left, y - parent.top, width, height,
            progman, null, null, null);

        if (_hwnd.IsNull)
        {
            throw new InvalidOperationException(
                $"Could not create the wallpaper window (Win32 error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}).");
        }

        // Fully opaque. The flag matters, not the value: without a layered
        // window with attributes set, this renders nothing at all.
        PInvoke.SetLayeredWindowAttributes(_hwnd, new COLORREF(0), 255, LAYERED_WINDOW_ATTRIBUTES_FLAGS.LWA_ALPHA);

        PInvoke.ShowWindow(_hwnd, SHOW_WINDOW_CMD.SW_SHOWNA);
        Reposition(); // again after showing: showing can reorder
    }

    public nint Handle => _hwnd;

    /// <summary>Whether the window still exists. False after Explorer restarts.</summary>
    public bool IsAlive => !_disposed && PInvoke.IsWindow(_hwnd);

    public (int Width, int Height) ClientSize
    {
        get
        {
            PInvoke.GetClientRect(_hwnd, out var rect);
            return (rect.right - rect.left, rect.bottom - rect.top);
        }
    }

    /// <summary>Re-inserts the window directly behind the icon layer.</summary>
    public void PinToBottom()
    {
        if (IsAlive)
        {
            Reposition();
        }
    }

    public void SetVisible(bool visible)
    {
        if (!IsAlive)
        {
            return;
        }

        PInvoke.ShowWindow(_hwnd, visible ? SHOW_WINDOW_CMD.SW_SHOWNA : SHOW_WINDOW_CMD.SW_HIDE);
        if (visible)
        {
            Reposition();
        }
    }

    private void Reposition()
    {
        var progman = PInvoke.GetParent(_hwnd);
        if (progman.IsNull)
        {
            return;
        }

        var defView = ProgmanLayer.FindDefView(progman);
        if (defView.IsNull)
        {
            return;
        }

        PInvoke.SetWindowPos(
            _hwnd,
            defView, // insert after the icon layer = directly behind it
            0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE
            | SET_WINDOW_POS_FLAGS.SWP_NOSIZE
            | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
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
            // Harmless if Explorer already took the window with it.
            PInvoke.DestroyWindow(_hwnd);
            _hwnd = HWND.Null;
        }
    }
}
