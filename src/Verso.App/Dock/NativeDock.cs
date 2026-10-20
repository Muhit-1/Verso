using System.Runtime.InteropServices;
using System.Text;

namespace Verso.App.Dock;

/// <summary>
/// The few Win32 calls the compact dock needs (window list, activation, icons). Hand-written here, like the
/// pop-up windows' focus helpers, because they are tiny and used by nothing else.
/// </summary>
internal static class NativeDock
{
    public delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

    public const int GWL_EXSTYLE = -20;
    public const int GWL_STYLE = -16;
    public const long WS_EX_TOOLWINDOW = 0x80;
    public const long WS_EX_APPWINDOW = 0x40000;
    public const long WS_EX_NOACTIVATE = 0x08000000;
    public const long WS_CAPTION = 0xC00000;
    public const int SW_MINIMIZE = 6;
    public const int SW_RESTORE = 9;
    public const int WM_GETICON = 0x007F;
    public const int WM_MOUSEACTIVATE = 0x0021;
    public const int GCLP_HICON = -14;
    public const uint SMTO_ABORTIFHUNG = 0x0002;
    public const int SM_CXSCREEN = 0;
    public const int SM_CYSCREEN = 1;

    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc proc, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] public static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint attach, uint to, bool on);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string? title);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] public static extern IntPtr GetClassLongPtr(IntPtr hwnd, int index);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
    [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern bool QueryFullProcessImageName(IntPtr process, uint flags, StringBuilder name, ref int size);

    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

    public static string Text(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string ClassOf(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static string? ExePath(uint pid)
    {
        var process = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (process == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var sb = new StringBuilder(1024);
            var size = sb.Capacity;
            return QueryFullProcessImageName(process, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    /// <summary>Brings a window to the front even though the click that asked for it came through another process's window.</summary>
    public static void Foreground(IntPtr hwnd)
    {
        var current = GetForegroundWindow();
        var currentThread = current == IntPtr.Zero ? 0 : GetWindowThreadProcessId(current, out _);
        var thisThread = GetCurrentThreadId();
        var attached = currentThread != 0 && currentThread != thisThread && AttachThreadInput(thisThread, currentThread, true);

        try
        {
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(thisThread, currentThread, false);
            }
        }
    }
}
