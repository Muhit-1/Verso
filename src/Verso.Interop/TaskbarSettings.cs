using Microsoft.Win32;
using Verso.Core.Models;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;

namespace Verso.Interop;

/// <summary>
/// Reads and writes the Windows taskbar options (position, alignment, size,
/// auto-hide, badges, ...) — the same values Windows' own Settings page edits.
/// </summary>
/// <remarks>
/// Measured on this machine (CLAUDE.md, "taskbar spike S0"): Explorer watches
/// the Advanced key and applies position, size, alignment, Task view and Search
/// live, with no broadcast needed. Auto-hide goes through the shell's own app-bar
/// message. The Widgets button (<c>TaskbarDa</c>) is write-protected by Windows,
/// so it is deliberately not here. Badges, flashing, share-window, far-corner and
/// combine are written under the names Windows uses but their effect was not
/// observable by the test, so they are best effort.
/// </remarks>
public static class TaskbarSettings
{
    private const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string SearchKey = @"Software\Microsoft\Windows\CurrentVersion\Search";

    private const uint ABM_GETSTATE = 4;
    private const uint ABM_SETSTATE = 10;
    private const int ABS_AUTOHIDE = 1;

    public static TaskbarState Read()
    {
        using var adv = Registry.CurrentUser.OpenSubKey(Advanced);
        using var search = Registry.CurrentUser.OpenSubKey(SearchKey);

        return new TaskbarState
        {
            Position = Math.Clamp(Dword(adv, "TaskbarLocation", 3), 0, 3),
            Align = Dword(adv, "TaskbarAl", 1) == 0 ? 0 : 1,
            Small = Dword(adv, "TaskbarSize", 0) == 1,
            Auto = GetAutoHide(),
            Badges = Dword(adv, "TaskbarBadges", 1) != 0,
            Flash = Dword(adv, "TaskbarFlashing", 1) != 0,
            Share = Dword(adv, "TaskbarSn", 1) != 0,
            Corner = Dword(adv, "TaskbarSd", 1) != 0,
            Combine = Math.Clamp(Dword(adv, "TaskbarGlomLevel", 0), 0, 2),
            Search = Dword(search, "SearchboxTaskbarMode", 1) != 0,
            TaskView = Dword(adv, "ShowTaskViewButton", 1) != 0
        };
    }

    /// <summary>Position, alignment and size only (three registry reads) — cheap enough for a timer.</summary>
    public static (int Position, int Align, bool Small) ReadLayout()
    {
        using var adv = Registry.CurrentUser.OpenSubKey(Advanced);
        return (Math.Clamp(Dword(adv, "TaskbarLocation", 3), 0, 3), Dword(adv, "TaskbarAl", 1) == 0 ? 0 : 1, Dword(adv, "TaskbarSize", 0) == 1);
    }

    /// <summary>
    /// Changes one option. Returns false for an unknown name or a value that makes
    /// no sense (for example auto-hide when the taskbar is not at the bottom, which
    /// Windows does not allow either).
    /// </summary>
    public static bool Set(string name, int value)
    {
        switch (name)
        {
            case "position":
                if (value is < 0 or > 3) return false;
                if (value != 3 && GetAutoHide()) SetAutoHide(false);
                return Write(Advanced, "TaskbarLocation", value);
            case "align": return Write(Advanced, "TaskbarAl", value == 0 ? 0 : 1);
            case "size": return Write(Advanced, "TaskbarSize", value == 1 ? 1 : 0);
            case "auto":
                if (value != 0 && Dword(Registry.CurrentUser.OpenSubKey(Advanced), "TaskbarLocation", 3) != 3) return false;
                SetAutoHide(value != 0);
                return true;
            case "badges": return Write(Advanced, "TaskbarBadges", value != 0 ? 1 : 0);
            case "flash": return Write(Advanced, "TaskbarFlashing", value != 0 ? 1 : 0);
            case "share": return Write(Advanced, "TaskbarSn", value != 0 ? 1 : 0);
            case "corner": return Write(Advanced, "TaskbarSd", value != 0 ? 1 : 0);
            case "combine": return value is >= 0 and <= 2 && Write(Advanced, "TaskbarGlomLevel", value);
            case "search":
                // 0 hidden, 1 icon, 2 box, 3 icon with label. "On" keeps a richer mode the user already chose.
                var now = Dword(Registry.CurrentUser.OpenSubKey(SearchKey), "SearchboxTaskbarMode", 1);
                return Write(SearchKey, "SearchboxTaskbarMode", value == 0 ? 0 : (now == 0 ? 1 : now));
            case "taskview": return Write(Advanced, "ShowTaskViewButton", value != 0 ? 1 : 0);
            default: return false;
        }
    }

    /// <summary>Puts every option back to the given state (used by "restore").</summary>
    public static void Apply(TaskbarState s)
    {
        Set("position", s.Position);
        Set("align", s.Align);
        Set("size", s.Small ? 1 : 0);
        Set("badges", s.Badges ? 1 : 0);
        Set("flash", s.Flash ? 1 : 0);
        Set("share", s.Share ? 1 : 0);
        Set("corner", s.Corner ? 1 : 0);
        Set("combine", s.Combine);
        Set("search", s.Search ? 1 : 0);
        Set("taskview", s.TaskView ? 1 : 0);
        Set("auto", s.Auto && s.Position == 3 ? 1 : 0);
    }

    public static bool GetAutoHide()
    {
        var data = Data();
        return data.hWnd != HWND.Null && ((int)PInvoke.SHAppBarMessage(ABM_GETSTATE, ref data) & ABS_AUTOHIDE) != 0;
    }

    private static void SetAutoHide(bool on)
    {
        var data = Data();
        if (data.hWnd == HWND.Null) return;
        data.lParam = new LPARAM(on ? ABS_AUTOHIDE : 0);
        PInvoke.SHAppBarMessage(ABM_SETSTATE, ref data);
    }

    private static APPBARDATA Data()
    {
        var data = new APPBARDATA { hWnd = PInvoke.FindWindow("Shell_TrayWnd", null) };
        data.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<APPBARDATA>();
        return data;
    }

    private static int Dword(RegistryKey? key, string name, int fallback) =>
        key?.GetValue(name) is int v ? v : fallback;

    private static bool Write(string path, string name, int value)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(path, writable: true);
            key.SetValue(name, value, RegistryValueKind.DWord);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
