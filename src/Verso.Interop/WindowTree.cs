using Windows.Win32;
using Windows.Win32.Foundation;

namespace Verso.Interop;

/// <summary>Small helpers for walking another window's children.</summary>
public static class WindowTree
{
    /// <summary>Whether <paramref name="window"/> still refers to a live window.</summary>
    public static bool Exists(nint window) => window != 0 && PInvoke.IsWindow((HWND)window);

    /// <summary>
    /// The first descendant of <paramref name="parent"/> with the given window class, searched depth
    /// first. WebView2 hosts its page in <c>Chrome_RenderWidgetHostHWND</c>, a few levels down inside
    /// the window it was given, and recreates it when the renderer is swapped — so look it up again
    /// rather than caching it for long.
    /// </summary>
    /// <returns>The window handle, or 0 if there is none (yet).</returns>
    public static nint FindDescendant(nint parent, string className)
    {
        if (parent == 0)
        {
            return 0;
        }

        for (var child = PInvoke.FindWindowEx((HWND)parent, HWND.Null, null, null);
             !child.IsNull;
             child = PInvoke.FindWindowEx((HWND)parent, child, null, null))
        {
            Span<char> name = stackalloc char[64];
            var length = PInvoke.GetClassName(child, name);
            if (name[..length].SequenceEqual(className))
            {
                return child;
            }

            var deeper = FindDescendant(child, className);
            if (deeper != 0)
            {
                return deeper;
            }
        }

        return 0;
    }
}
