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
/// exists. Microsoft never promised any of it works.
/// </para>
/// <para>
/// There is no single technique, because Explorer has used three different
/// desktop window layouts over the years and which one you get depends on the
/// Windows build, whether the wallpaper is a slideshow, and whether desktop
/// icons are shown at all. <see cref="Attach"/> tries all three in order and
/// reports which one it landed on.
/// </para>
/// <list type="number">
///   <item><b>WorkerW sibling</b> (Windows 10, and 11 before ~22H2). Sending the
///   undocumented message <c>0x052C</c> to <c>Progman</c> makes Explorer split
///   the desktop and spawn a top-level <c>WorkerW</c> for the wallpaper, sitting
///   just after the icon layer in Z-order.</item>
///   <item><b>Progman's child WorkerW</b> (current Windows 11). No top-level
///   <c>WorkerW</c> ever appears and <c>0x052C</c> does nothing. Instead
///   <c>Progman</c> owns two children: <c>SHELLDLL_DefView</c> in front, holding
///   the icons, and a <c>WorkerW</c> behind it holding the wallpaper. That
///   <c>WorkerW</c> is the target.</item>
///   <item><b>Progman itself</b>. Last resort. Works, but only because of the
///   <c>HWND_BOTTOM</c> call below.</item>
/// </list>
/// <para>
/// Whichever strategy wins, the child is pushed to the bottom of its new
/// parent's Z-order afterwards. This is not optional. <c>SetParent</c> inserts
/// at the <em>front</em>, so without it strategy 3 puts the wallpaper on top of
/// the desktop icons — which looks like the app is broken, and is the single
/// easiest mistake to make here.
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
    /// Undocumented. Asks Explorer to spawn the wallpaper <c>WorkerW</c>.
    /// Not in the Windows metadata, so it cannot come from NativeMethods.txt.
    /// </summary>
    private const uint WM_SPAWN_WORKER = 0x052C;

    /// <summary>
    /// <c>HWND_BOTTOM</c>. A sentinel value for <c>SetWindowPos</c> rather than a
    /// real handle, so the Windows metadata does not carry it and CsWin32 cannot
    /// generate it.
    /// </summary>
    private static HWND BottomOfZOrder => (HWND)(nint)1;

    /// <summary>Whether the WorkerW technique can produce a visible layer here.</summary>
    public enum DesktopLayout
    {
        /// <summary>
        /// Explorer split the desktop: a visible top-level <c>WorkerW</c> exists
        /// for the wallpaper. Attaching into it works, and Verso can sit
        /// genuinely behind the desktop icons.
        /// </summary>
        Splittable,

        /// <summary>
        /// Explorer did not split. <c>SHELLDLL_DefView</c> stays under
        /// <c>Progman</c> and no visible top-level <c>WorkerW</c> is ever
        /// created. A window parented into Progman's own child <c>WorkerW</c> is
        /// reported as visible and receives <c>WM_PAINT</c>, but its pixels never
        /// reach the screen — so this case must NOT be treated as a success.
        /// Use <see cref="BottomMostLayer"/> instead.
        /// </summary>
        NotSplittable,

        /// <summary>Explorer is not running.</summary>
        NoShell
    }

    /// <summary>
    /// Works out whether the <c>WorkerW</c> technique will actually produce
    /// something visible on this machine.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Call this before <see cref="Attach"/>. The distinction it draws is not
    /// cosmetic: on Windows 11 builds where the split no longer happens,
    /// <see cref="Attach"/> still succeeds — it finds Progman's child
    /// <c>WorkerW</c>, parents into it, and reports the window as visible and
    /// full-size — while nothing is ever drawn on screen. Trusting
    /// <see cref="Attach"/> alone is how you spend an evening debugging a
    /// wallpaper that "works" and cannot be seen.
    /// </para>
    /// <para>
    /// The test is the presence of a <em>visible top-level</em> <c>WorkerW</c>
    /// after the split message. Invisible ones are always present and mean
    /// nothing.
    /// </para>
    /// </remarks>
    public static DesktopLayout DetectLayout()
    {
        var progman = PInvoke.FindWindow("Progman", null);
        if (progman.IsNull)
        {
            return DesktopLayout.NoShell;
        }

        PInvoke.SendMessageTimeout(
            progman, WM_SPAWN_WORKER,
            (WPARAM)(nuint)0xD, (LPARAM)(nint)0x1,
            SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_NORMAL, uTimeout: 1000);

        return HasVisibleTopLevelWorkerW()
            ? DesktopLayout.Splittable
            : DesktopLayout.NotSplittable;
    }

    private static bool HasVisibleTopLevelWorkerW()
    {
        var found = false;

        PInvoke.EnumWindows((hwnd, _) =>
        {
            Span<char> buffer = stackalloc char[64];
            var length = PInvoke.GetClassName(hwnd, buffer);
            if (length == 7 && buffer[..7].SequenceEqual("WorkerW")
                && PInvoke.IsWindowVisible(hwnd))
            {
                found = true;
                return false;
            }

            return true;
        },
        (LPARAM)(nint)0);

        return found;
    }

    /// <summary>Which desktop layout the machine turned out to have.</summary>
    public enum AttachStrategy
    {
        /// <summary>Nothing worked. Explorer is probably not running.</summary>
        None,

        /// <summary>Top-level WorkerW after the icon layer. Windows 10.</summary>
        WorkerWSibling,

        /// <summary>Progman's own child WorkerW, behind SHELLDLL_DefView. Windows 11.</summary>
        ProgmanChildWorkerW,

        /// <summary>Progman itself. Last resort, and relies on the Z-order fix.</summary>
        Progman
    }

    /// <summary>What happened, in enough detail to put in a bug report.</summary>
    /// <param name="Success">Whether the window is now in the desktop layer.</param>
    /// <param name="Strategy">Which layout the machine turned out to have.</param>
    /// <param name="Parent">The window we parented into.</param>
    /// <param name="BehindIcons">
    /// Whether the desktop icons are confirmed to be in front of us. False means
    /// the attach worked but the icons may be covered — worth reporting.
    /// </param>
    /// <param name="Diagnostics">One line describing the desktop window stack.</param>
    public readonly record struct AttachOutcome(
        bool Success,
        AttachStrategy Strategy,
        nint Parent,
        bool BehindIcons,
        string Diagnostics);

    /// <summary>
    /// Attaches <paramref name="child"/> behind the desktop icons.
    /// </summary>
    /// <param name="child">A real HWND. From WPF: <c>new WindowInteropHelper(w).Handle</c>.</param>
    /// <remarks>
    /// Never throws. A failure here has to degrade into "Verso runs but is not
    /// the wallpaper", not into a crash on somebody's desktop.
    /// </remarks>
    public static AttachOutcome Attach(nint child)
    {
        var progman = PInvoke.FindWindow("Progman", null);
        if (progman.IsNull)
        {
            return new AttachOutcome(false, AttachStrategy.None, 0, false, Describe());
        }

        // Harmless where it does nothing, required on Windows 10. Explorer may
        // answer before the WorkerW actually exists, so the result is ignored
        // and we look for the window afterwards rather than trusting the reply.
        PInvoke.SendMessageTimeout(
            progman, WM_SPAWN_WORKER,
            (WPARAM)(nuint)0xD, (LPARAM)(nint)0x1,
            SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_NORMAL, uTimeout: 1000);

        var (target, strategy) = FindTarget(progman);

        Marshal.SetLastSystemError(0);
        var previous = PInvoke.SetParent((HWND)child, target);

        // SetParent returns the previous parent, and null is also a legitimate
        // previous parent — so clear the error first and read it back rather
        // than trusting the return value alone.
        if (previous.IsNull && Marshal.GetLastWin32Error() != 0)
        {
            return new AttachOutcome(false, strategy, target, false, Describe());
        }

        SendToBack(child);

        return new AttachOutcome(
            Success: true,
            Strategy: strategy,
            Parent: target,
            BehindIcons: IsBehindIcons(child),
            Diagnostics: Describe());
    }

    /// <summary>
    /// Pushes a window to the back of its parent's Z-order.
    /// </summary>
    /// <remarks>
    /// Called after every attach, and again whenever Explorer rearranges the
    /// desktop. <c>SetParent</c> inserts at the front, so skipping this puts the
    /// wallpaper on top of the desktop icons.
    /// </remarks>
    public static void SendToBack(nint hwnd)
    {
        PInvoke.SetWindowPos(
            (HWND)hwnd,
            BottomOfZOrder,
            0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE
            | SET_WINDOW_POS_FLAGS.SWP_NOSIZE
            | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE);
    }

    /// <summary>Detaches a window from the desktop layer, back to the shell.</summary>
    /// <remarks>
    /// Not needed on shutdown — destroying the window is enough. It matters when
    /// switching a display from wallpaper mode back to normal without recreating
    /// the window.
    /// </remarks>
    public static void Detach(nint hwnd) => PInvoke.SetParent((HWND)hwnd, HWND.Null);

    /* ---------------------------------------------------------------- */

    private static (HWND Target, AttachStrategy Strategy) FindTarget(HWND progman)
    {
        var sibling = FindSiblingWorkerW();
        if (!sibling.IsNull)
        {
            return (sibling, AttachStrategy.WorkerWSibling);
        }

        var child = FindProgmanChildWorkerW(progman);
        if (!child.IsNull)
        {
            return (child, AttachStrategy.ProgmanChildWorkerW);
        }

        return (progman, AttachStrategy.Progman);
    }

    /// <summary>
    /// The top-level <c>WorkerW</c> that follows the icon layer in Z-order.
    /// Null on current Windows 11, where no such window is ever created.
    /// </summary>
    private static HWND FindSiblingWorkerW()
    {
        HWND found = HWND.Null;

        PInvoke.EnumWindows((hwnd, _) =>
        {
            // The icon layer is whichever top-level window owns SHELLDLL_DefView.
            if (PInvoke.FindWindowEx(hwnd, HWND.Null, "SHELLDLL_DefView", null).IsNull)
            {
                return true; // keep enumerating
            }

            found = PInvoke.FindWindowEx(HWND.Null, hwnd, "WorkerW", null);
            return found.IsNull; // stop once we have it
        },
        (LPARAM)(nint)0);

        return found;
    }

    /// <summary>
    /// Progman's own <c>WorkerW</c> child — the wallpaper layer on current
    /// Windows 11, sitting behind <c>SHELLDLL_DefView</c>.
    /// </summary>
    /// <remarks>
    /// Progman can own more than one <c>WorkerW</c>, and on some builds one of
    /// them hosts the icons instead. Skip any that contains a
    /// <c>SHELLDLL_DefView</c>: that one is the icon layer, and parenting into
    /// it would put the wallpaper in front of the icons.
    /// </remarks>
    private static HWND FindProgmanChildWorkerW(HWND progman)
    {
        var candidate = PInvoke.FindWindowEx(progman, HWND.Null, "WorkerW", null);

        while (!candidate.IsNull)
        {
            if (PInvoke.FindWindowEx(candidate, HWND.Null, "SHELLDLL_DefView", null).IsNull)
            {
                return candidate;
            }

            candidate = PInvoke.FindWindowEx(progman, candidate, "WorkerW", null);
        }

        return HWND.Null;
    }

    /// <summary>
    /// Whether the desktop icons are in front of <paramref name="hwnd"/>.
    /// </summary>
    /// <remarks>
    /// Walks the sibling chain forwards from our window; if it reaches the icon
    /// layer, the icons are in front and all is well. Cheap, and it turns "the
    /// wallpaper covered my icons" from a visual surprise into a value the spike
    /// can print.
    /// </remarks>
    private static bool IsBehindIcons(nint hwnd)
    {
        var defView = FindDefView();
        if (defView.IsNull)
        {
            return true; // no icons shown at all; nothing to be in front of
        }

        // Anything not a sibling of the icon layer is in a different window
        // tree entirely, which means it is already behind it.
        var parent = PInvoke.GetParent((HWND)hwnd);
        if (parent != PInvoke.GetParent(defView))
        {
            return true;
        }

        for (var w = PInvoke.GetWindow((HWND)hwnd, GET_WINDOW_CMD.GW_HWNDPREV);
             !w.IsNull;
             w = PInvoke.GetWindow(w, GET_WINDOW_CMD.GW_HWNDPREV))
        {
            if (w == defView)
            {
                return true;
            }
        }

        return false;
    }

    private static HWND FindDefView()
    {
        HWND found = HWND.Null;

        PInvoke.EnumWindows((hwnd, _) =>
        {
            var defView = PInvoke.FindWindowEx(hwnd, HWND.Null, "SHELLDLL_DefView", null);
            if (defView.IsNull)
            {
                return true;
            }

            found = defView;
            return false;
        },
        (LPARAM)(nint)0);

        return found;
    }

    /// <summary>Class name of a window. Empty on failure. Diagnostics only.</summary>
    public static string GetClassName(nint hwnd)
    {
        if (hwnd == 0)
        {
            return string.Empty;
        }

        Span<char> buffer = stackalloc char[256];
        var length = PInvoke.GetClassName((HWND)hwnd, buffer);
        return length > 0 ? new string(buffer[..length]) : string.Empty;
    }

    /// <summary>
    /// A one-line description of the desktop window stack. Paste this into a bug
    /// report when the wallpaper stops appearing after a Windows update — it is
    /// the fastest way to see which of the three layouts the machine has.
    /// </summary>
    public static string Describe()
    {
        var sb = new StringBuilder();

        var progman = PInvoke.FindWindow("Progman", null);
        sb.Append("Progman=0x").Append(((nint)progman).ToString("X"));

        if (progman.IsNull)
        {
            return sb.Append(" (Explorer not running?)").ToString();
        }

        sb.Append("  DefView-under-Progman=")
          .Append(PInvoke.FindWindowEx(progman, HWND.Null, "SHELLDLL_DefView", null).IsNull ? "no" : "yes");

        var sibling = FindSiblingWorkerW();
        sb.Append("  sibling-WorkerW=")
          .Append(sibling.IsNull ? "none" : "0x" + ((nint)sibling).ToString("X"));

        var child = FindProgmanChildWorkerW(progman);
        sb.Append("  Progman-child-WorkerW=")
          .Append(child.IsNull ? "none" : "0x" + ((nint)child).ToString("X"));

        return sb.ToString();
    }
}
