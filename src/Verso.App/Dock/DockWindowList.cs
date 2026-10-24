using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Verso.App.Dock;

/// <summary>One dock icon: all open windows of one app (or a single window for Store apps).</summary>
internal sealed class DockEntry
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public required List<IntPtr> Windows { get; init; }
    public required bool AllMinimised { get; init; }
    public string? Exe { get; init; }
}

/// <summary>
/// The list of "apps that are open right now": what Alt-Tab shows, grouped per program, in a stable order
/// (the order they first appeared — z-order would shuffle the dock every time you switch window).
/// Measured against the real taskbar: same windows (CLAUDE.md, "taskbar spike S0").
/// </summary>
internal sealed class DockWindowList
{
    private readonly List<string> _order = [];
    private readonly Dictionary<string, ImageSource?> _icons = [];
    private readonly uint _ownPid = (uint)Environment.ProcessId;

    public List<DockEntry> Snapshot(bool includeMinimised)
    {
        var windows = new List<(IntPtr hwnd, string title, string cls, uint pid)>();

        NativeDock.EnumWindows((hwnd, _) =>
        {
            if (IsAppWindow(hwnd, out var title, out var cls, out var pid))
            {
                windows.Add((hwnd, title, cls, pid));
            }

            return true;
        }, IntPtr.Zero);

        var exeByPid = new Dictionary<uint, string?>();
        var groups = new Dictionary<string, List<(IntPtr hwnd, string title)>>();
        var exeOfKey = new Dictionary<string, string?>();

        foreach (var (hwnd, title, cls, pid) in windows)
        {
            if (!includeMinimised && NativeDock.IsIconic(hwnd))
            {
                continue;
            }

            if (!exeByPid.TryGetValue(pid, out var exe))
            {
                exeByPid[pid] = exe = NativeDock.ExePath(pid);
            }

            // Store apps all run inside ApplicationFrameHost.exe: group those per window, not per exe.
            var key = exe is null || exe.EndsWith("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase)
                ? $"w{hwnd}"
                : exe.ToLowerInvariant();

            if (!groups.TryGetValue(key, out var list))
            {
                groups[key] = list = [];
                exeOfKey[key] = exe;
            }

            list.Add((hwnd, title));
        }

        _order.RemoveAll(k => !groups.ContainsKey(k));
        foreach (var key in groups.Keys)
        {
            if (!_order.Contains(key))
            {
                _order.Add(key);
            }
        }

        return _order.Select(k => new DockEntry
        {
            Key = k,
            Title = groups[k][0].title,
            Windows = groups[k].Select(w => w.hwnd).ToList(),
            AllMinimised = groups[k].All(w => NativeDock.IsIconic(w.hwnd)),
            Exe = exeOfKey[k]
        }).ToList();
    }

    private bool IsAppWindow(IntPtr hwnd, out string title, out string cls, out uint pid)
    {
        title = string.Empty;
        cls = string.Empty;
        pid = 0;

        if (!NativeDock.IsWindowVisible(hwnd))
        {
            return false;
        }

        var ex = (long)NativeDock.GetWindowLongPtr(hwnd, NativeDock.GWL_EXSTYLE);
        var tool = (ex & NativeDock.WS_EX_TOOLWINDOW) != 0;
        var appWindow = (ex & NativeDock.WS_EX_APPWINDOW) != 0;
        if (tool && !appWindow)
        {
            return false;
        }

        if (NativeDock.GetWindow(hwnd, 4 /* GW_OWNER */) != IntPtr.Zero && !appWindow)
        {
            return false;
        }

        // Cloaked = a Store app's hidden frame or a window on another virtual desktop: not "open" for the user.
        if (NativeDock.DwmGetWindowAttribute(hwnd, 14 /* DWMWA_CLOAKED */, out var cloaked, sizeof(int)) == 0 && cloaked != 0)
        {
            return false;
        }

        title = NativeDock.Text(hwnd);
        if (title.Length == 0)
        {
            return false;
        }

        cls = NativeDock.ClassOf(hwnd);
        if (cls is "Progman" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "WorkerW")
        {
            return false;
        }

        NativeDock.GetWindowThreadProcessId(hwnd, out pid);

        // Verso's own pop-ups (editor, picker, the dock itself) are not apps; its dashboard is.
        return pid != _ownPid || title == "Verso";
    }

    /// <summary>The app's own icon from the window (what the taskbar shows), else the program's file icon.</summary>
    public ImageSource? IconFor(DockEntry entry)
    {
        if (_icons.TryGetValue(entry.Key, out var cached) && cached is not null)
        {
            return cached;
        }

        ImageSource? icon = null;
        try
        {
            foreach (var hwnd in entry.Windows)
            {
                var handle = IconHandle(hwnd);
                if (handle != IntPtr.Zero)
                {
                    icon = FromHandle(handle);
                    break;
                }
            }

            if (icon is null && entry.Exe is not null && File.Exists(entry.Exe))
            {
                using var file = System.Drawing.Icon.ExtractAssociatedIcon(entry.Exe);
                if (file is not null)
                {
                    icon = FromHandle(file.Handle);
                }
            }
        }
        catch (Exception ex)
        {
            // fall back to the generic tile
            Hosting.Log.Warn($"dock: no icon for {entry.Key}", ex);
        }

        _icons[entry.Key] = icon;
        return icon;
    }

    private static IntPtr IconHandle(IntPtr hwnd)
    {
        foreach (var size in new[] { 1 /* ICON_BIG */, 2 /* ICON_SMALL2 */, 0 /* ICON_SMALL */ })
        {
            if (NativeDock.SendMessageTimeout(hwnd, NativeDock.WM_GETICON, (IntPtr)size, IntPtr.Zero, NativeDock.SMTO_ABORTIFHUNG, 100, out var result) != IntPtr.Zero
                && result != IntPtr.Zero)
            {
                return result;
            }
        }

        return NativeDock.GetClassLongPtr(hwnd, NativeDock.GCLP_HICON);
    }

    private static ImageSource FromHandle(IntPtr hIcon)
    {
        var source = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        source.Freeze();
        return source;
    }
}
