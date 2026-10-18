using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Verso.Interop;

/// <summary>
/// Raises <see cref="ShellRestarted"/> when Explorer comes back after being
/// killed or crashing.
/// </summary>
/// <remarks>
/// <para>
/// Anything parented into Progman — which is what <see cref="ProgmanSurface"/>
/// is — is destroyed with it. Explorer announces its return by broadcasting the
/// registered <c>TaskbarCreated</c> message to every top-level window. Measured
/// on build 26300: the broadcast arrives within a couple of seconds of the
/// shell being back.
/// </para>
/// <para>
/// Needs a real, hidden top-level window: message-only windows do not receive
/// broadcasts. Runs on the UI thread through the WPF dispatcher, like
/// <see cref="DesktopSurface"/>, so the event is raised there.
/// </para>
/// </remarks>
public sealed unsafe class ShellRestartWatcher : IDisposable
{
    private const string ClassName = "VersoShellWatcher";

    private static WNDPROC? _wndProc;
    private static ushort _classAtom;
    private static uint _taskbarCreated;
    private static ShellRestartWatcher? _current;

    private HWND _hwnd;

    public ShellRestartWatcher()
    {
        EnsureClassRegistered();

        _hwnd = PInvoke.CreateWindowEx(
            default, ClassName, "Verso shell watcher",
            WINDOW_STYLE.WS_POPUP,
            0, 0, 0, 0,
            HWND.Null, null, null, null);

        if (_hwnd.IsNull)
        {
            throw new InvalidOperationException(
                $"Could not create the shell watcher window (Win32 error {Marshal.GetLastWin32Error()}).");
        }

        _current = this;
    }

    /// <summary>Raised on the UI thread each time Explorer announces a fresh taskbar.</summary>
    public event Action? ShellRestarted;

    private static void EnsureClassRegistered()
    {
        if (_classAtom != 0)
        {
            return;
        }

        _taskbarCreated = PInvoke.RegisterWindowMessage("TaskbarCreated");

        _wndProc = static (hwnd, msg, wParam, lParam) =>
        {
            if (msg == _taskbarCreated)
            {
                _current?.ShellRestarted?.Invoke();
            }

            return PInvoke.DefWindowProc(hwnd, msg, wParam, lParam);
        };

        fixed (char* className = ClassName)
        {
            var wc = new WNDCLASSEXW
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                lpfnWndProc = _wndProc,
                hInstance = (HINSTANCE)(nint)PInvoke.GetModuleHandle((PCWSTR)null),
                lpszClassName = new PCWSTR(className)
            };

            _classAtom = PInvoke.RegisterClassEx(in wc);
        }

        if (_classAtom == 0)
        {
            throw new InvalidOperationException(
                $"Could not register the shell watcher class (Win32 error {Marshal.GetLastWin32Error()}).");
        }
    }

    public void Dispose()
    {
        if (_hwnd.IsNull)
        {
            return;
        }

        if (ReferenceEquals(_current, this))
        {
            _current = null;
        }

        PInvoke.DestroyWindow(_hwnd);
        _hwnd = HWND.Null;
    }
}
