using System.Runtime.InteropServices;
using System.Text;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Verso.Interop;

/// <summary>
/// Puts a window behind the desktop icons.
/// </summary>
/// <remarks>
/// <para>
/// This is the one undocumented thing Verso depends on, and the reason Phase 0
/// exists. The technique:
/// </para>
/// <list type="number">
///   <item>Send the undocumented message <c>0x052C</c> to <c>Progman</c>. That
///   asks Explorer to split the desktop into two layers and spawn a
///   <c>WorkerW</c> window for the wallpaper.</item>
///   <item>Walk the top-level windows for the one owning a
///   <c>SHELLDLL_DefView</c> child — that is the icon layer. The <c>WorkerW</c>
///   that is its next sibling is the wallpaper layer.</item>
///   <item><c>SetParent</c> our window into that <c>WorkerW</c>.</item>
/// </list>
/// <para>
/// Microsoft never promised any of this works. It has survived a decade, but it
/// is the one thing in the app that can break on a Tuesday, so
/// <see cref="TryAttach"/> reports failure rather than throwing and every caller
/// is expected to degrade gracefully.
/// </para>
/// <para>
/// On some Windows 11 builds Explorer keeps <c>SHELLDLL_DefView</c> under
/// <c>Progman</c> itself and never spawns a separate <c>WorkerW</c>. That is not
/// an error — we parent into <c>Progman</c> instead.
/// </para>
/// <para>
/// Handles cross this class's public surface as plain <see cref="nint"/>.
/// CsWin32's generated types are internal to this assembly, and keeping them
/// here is what stops Win32 leaking into the rest of the app.
/// </para>
/// </remarks>
public static class DesktopLayer
{
    /// <summary>
    /// Undocumented. Makes Explorer spawn the wallpaper <c>WorkerW</c>.
    /// Not in the Windows metadata, so it cannot come from NativeMethods.txt.
    /// </summary>
    private const uint WM_SPAWN_WORKER = 0x052C;

    /// <summary>Where a window ended up, and how.</summary>
    public enum AttachResult
    {
        /// <summary>Parented into a real WorkerW. The normal path.</summary>
        AttachedToWorkerW,

        /// <summary>No WorkerW appeared; parented into Progman. Also fine.</summary>
        AttachedToProgman,

        /// <summary>Progman was not found at all. Explorer is probably not running.</summary>
        ProgmanNotFound,

        /// <summary>Found the parent but SetParent refused.</summary>
        SetParentFailed
    }

    /// <summary>
    /// Attaches <paramref name="child"/> behind the desktop icons.
    /// </summary>
    /// <param name="child">A real HWND. From WPF: <c>new WindowInteropHelper(w).Handle</c>.</param>
    /// <param name="parent">The window we parented into, for diagnostics.</param>
    public static AttachResult TryAttach(nint child, out nint parent)
    {
        parent = 0;

        var progman = PInvoke.FindWindow("Progman", null);
        if (progman.IsNull)
        {
            return AttachResult.ProgmanNotFound;
        }

        // Ask Explorer for the wallpaper layer. The 0xD/0x1 payload is what
        // Windows 10 1709 and later expect. The result is ignored because
        // Explorer answers before the WorkerW necessarily exists.
        PInvoke.SendMessageTimeout(
            progman,
            WM_SPAWN_WORKER,
            (WPARAM)(nuint)0xD,
            (LPARAM)(nint)0x1,
            SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_NORMAL,
            uTimeout: 1000);

        var workerW = FindWorkerW();
        var target = workerW.IsNull ? progman : workerW;

        // SetParent returns the previous parent, or null on failure. Null is
        // also a legitimate previous parent, so clear the error first and read
        // it back rather than trusting the return value alone.
        Marshal.SetLastSystemError(0);
        var previous = PInvoke.SetParent((HWND)child, target);
        if (previous.IsNull && Marshal.GetLastWin32Error() != 0)
        {
            return AttachResult.SetParentFailed;
        }

        parent = target;
        return workerW.IsNull ? AttachResult.AttachedToProgman : AttachResult.AttachedToWorkerW;
    }

    /// <summary>
    /// Handle of the <c>WorkerW</c> that sits underneath the icon layer, or 0 if
    /// Explorer did not make one. Diagnostics only — <see cref="TryAttach"/>
    /// finds it itself.
    /// </summary>
    public static nint FindWallpaperWorkerW() => FindWorkerW();

    private static HWND FindWorkerW()
    {
        HWND found = HWND.Null;

        PInvoke.EnumWindows((hwnd, _) =>
        {
            // The icon layer is whichever top-level window owns SHELLDLL_DefView.
            var shellView = PInvoke.FindWindowEx(hwnd, HWND.Null, "SHELLDLL_DefView", null);
            if (shellView.IsNull)
            {
                return true; // keep enumerating
            }

            // The wallpaper WorkerW is that window's next sibling.
            found = PInvoke.FindWindowEx(HWND.Null, hwnd, "WorkerW", null);
            return found.IsNull; // stop once we have it
        },
        (LPARAM)(nint)0);

        return found;
    }

    /// <summary>Class name of a window. Empty on failure. Diagnostics only.</summary>
    public static string GetClassName(nint hwnd)
    {
        Span<char> buffer = stackalloc char[256];
        var length = PInvoke.GetClassName((HWND)hwnd, buffer);
        return length > 0 ? new string(buffer[..length]) : string.Empty;
    }

    /// <summary>
    /// A one-line description of the desktop window stack. Paste this into a bug
    /// report when the wallpaper stops appearing after a Windows update.
    /// </summary>
    public static string DescribeDesktop()
    {
        var sb = new StringBuilder();

        var progman = PInvoke.FindWindow("Progman", null);
        sb.Append("Progman=0x").Append(((nint)progman).ToString("X"));

        var defViewUnderProgman = PInvoke.FindWindowEx(progman, HWND.Null, "SHELLDLL_DefView", null);
        sb.Append("  DefView-under-Progman=")
          .Append(defViewUnderProgman.IsNull ? "no" : "yes");

        var workerW = FindWorkerW();
        sb.Append("  WorkerW=")
          .Append(workerW.IsNull ? "none" : "0x" + ((nint)workerW).ToString("X"));

        return sb.ToString();
    }
}
