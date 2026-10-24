using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Verso.Interop;

/// <summary>A window the mouse can be mirrored into: a WebView2 living behind the desktop icons.</summary>
public interface IInputTarget
{
    /// <summary>False while the desk is paused or hidden — nothing is mirrored then.</summary>
    bool IsActive { get; }

    /// <summary>The screen rectangle this target covers, in physical pixels.</summary>
    Rectangle ScreenBounds { get; }

    /// <summary>WebView2's own render window, which mouse messages are posted to. 0 until it exists.</summary>
    nint RenderWindow { get; }

    /// <summary>A wheel notch at (<paramref name="clientX"/>, <paramref name="clientY"/>), physical pixels.</summary>
    void Wheel(int clientX, int clientY, int delta);

    /// <summary>The pointer left the bare desktop (moved onto a window or the taskbar).</summary>
    void PointerLeft();
}

/// <summary>
/// Mirrors the real mouse into WebView2 windows that sit <b>behind the desktop icons</b>, where
/// Windows gives them no input of their own.
/// </summary>
/// <remarks>
/// <para>
/// Proven by <c>tools/Verso.Spike.Layers --input</c> on build 26300 (see the project notes, "W0 result"). The
/// mechanism is a <b>Raw Input</b> listener registered with <c>RIDEV_INPUTSINK</c> — passive: no
/// <c>WH_MOUSE_LL</c> hook, nothing is swallowed, Explorer still receives every click — plus
/// <c>PostMessage</c> of the matching <c>WM_MOUSE*</c> messages to the page's
/// <c>Chrome_RenderWidgetHostHWND</c>. Messages are only posted while the cursor is over the bare
/// desktop (its top-level window is Progman or WorkerW), so a window or the taskbar in front hides
/// the desk from the mouse exactly as it should.
/// </para>
/// <para>
/// What the page cannot rely on, measured: <c>:hover</c> and <c>mouseenter</c>/<c>mouseleave</c>
/// flicker (Windows sends WM_MOUSELEAVE because the real cursor is not over WebView's window), and a
/// posted <c>WM_MOUSEWHEEL</c> never arrives. So hover must be derived from pointer coordinates, and
/// the wheel is handed to the target to inject through script (<see cref="IInputTarget.Wheel"/>).
/// </para>
/// <para>
/// Idle cost is zero — it only runs when the mouse moves. While the mouse is moving it is throttled to
/// about 120 moves a second.
/// </para>
/// </remarks>
public sealed unsafe class InputMirror : IDisposable
{
    private const string ClassName = "VersoInputMirror";

    private const uint WM_INPUT = 0x00FF;
    private const uint WM_MOUSEMOVE = 0x0200;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const uint WM_MOUSELEAVE = 0x02A3;

    private static WNDPROC? _wndProc;
    private static ushort _classAtom;
    private static InputMirror? _current;

    private readonly Func<IReadOnlyList<IInputTarget>> _targets;
    private HWND _hwnd;
    private bool _registered;
    private bool _leftDown;
    private IInputTarget? _pressTarget;
    private IInputTarget? _over;
    private long _lastMoveTicks;

    /// <param name="targets">The current targets. Called on every mouse event, so keep it cheap.</param>
    public InputMirror(Func<IReadOnlyList<IInputTarget>> targets)
    {
        _targets = targets;
        EnsureClassRegistered();

        _hwnd = PInvoke.CreateWindowEx(
            default, ClassName, "Verso input mirror",
            WINDOW_STYLE.WS_POPUP,
            0, 0, 0, 0,
            HWND.Null, null, null, null);

        if (_hwnd.IsNull)
        {
            throw new InvalidOperationException(
                $"Could not create the input mirror window (Win32 error {Marshal.GetLastWin32Error()}).");
        }

        _current = this;

        // usage page 1 (generic desktop), usage 2 (mouse), RIDEV_INPUTSINK so it arrives in the
        // background and needs no focus.
        var device = new RawInputDevice { UsagePage = 1, Usage = 2, Flags = RIDEV_INPUTSINK, Target = _hwnd };
        _registered = RegisterRawInputDevices(&device, 1, (uint)sizeof(RawInputDevice));
    }

    /// <summary>False if Windows refused the Raw Input registration; the desk then shows but is not clickable.</summary>
    public bool IsListening => _registered;

    private static void EnsureClassRegistered()
    {
        if (_classAtom != 0)
        {
            return;
        }

        _wndProc = static (hwnd, msg, wParam, lParam) =>
        {
            if (msg == WM_INPUT)
            {
                try
                {
                    _current?.OnRawInput(lParam);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // A mouse event must never take the desk down.
                    Debug.WriteLine($"InputMirror: {ex}");
                }
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
                $"Could not register the input mirror class (Win32 error {Marshal.GetLastWin32Error()}).");
        }
    }

    private void OnRawInput(nint hRawInput)
    {
        // RAWINPUT: 24-byte header, then RAWMOUSE { usFlags, usButtonFlags, usButtonData, ... }.
        uint size = 0;
        GetRawInputData(hRawInput, RID_INPUT, null, ref size, HeaderSize);
        if (size == 0 || size > 256)
        {
            return;
        }

        Span<byte> buffer = stackalloc byte[(int)size];
        fixed (byte* p = buffer)
        {
            if (GetRawInputData(hRawInput, RID_INPUT, p, ref size, HeaderSize) == uint.MaxValue)
            {
                return;
            }
        }

        if (BitConverter.ToUInt32(buffer) != 0)
        {
            return; // not a mouse
        }

        var buttonFlags = BitConverter.ToUInt16(buffer[28..]);
        var buttonData = BitConverter.ToInt16(buffer[30..]);

        var targets = _targets();
        if (targets.Count == 0)
        {
            return;
        }

        PInvoke.GetCursorPos(out var cursor);
        var overDesktop = IsBareDesktop(cursor);

        IInputTarget? target = null;
        if (overDesktop)
        {
            foreach (var t in targets)
            {
                if (t.IsActive && t.ScreenBounds.Contains(cursor.X, cursor.Y))
                {
                    target = t;
                    break;
                }
            }
        }

        // A press we delivered must always get its release, wherever the cursor is by then.
        if ((buttonFlags & 0x2) != 0 && _leftDown)
        {
            _leftDown = false;
            if (_pressTarget is { } pressed)
            {
                // The final position first, then the release: pages that drag from pointermove (the
                // desk does) must see where the pointer ended up before it is let go.
                Post(pressed, WM_MOUSEMOVE, 1, cursor);
                Post(pressed, WM_LBUTTONUP, 0, cursor);
            }

            _pressTarget = null;
        }

        if (target is null)
        {
            if (_over is not null)
            {
                var render = _over.RenderWindow;
                if (render != 0)
                {
                    PInvoke.PostMessage((HWND)render, WM_MOUSELEAVE, 0, 0);
                }

                _over.PointerLeft();
                _over = null;
            }

            return;
        }

        if (!ReferenceEquals(_over, target))
        {
            _over?.PointerLeft();
            _over = target;
        }

        if ((buttonFlags & 0x1) != 0)
        {
            _leftDown = true;
            _pressTarget = target;
            Post(target, WM_LBUTTONDOWN, 1, cursor);
        }

        if ((buttonFlags & 0x400) != 0)
        {
            var client = ToClient(target, cursor);
            if (client is { } c)
            {
                target.Wheel(c.X, c.Y, buttonData);
            }
        }

        var now = Stopwatch.GetTimestamp();
        if (now - _lastMoveTicks >= Stopwatch.Frequency / 120)
        {
            _lastMoveTicks = now;
            Post(target, WM_MOUSEMOVE, _leftDown ? 1u : 0u, cursor);
        }
    }

    /// <summary>Whether the topmost window under the cursor belongs to the desktop (not an app or the taskbar).</summary>
    private static bool IsBareDesktop(System.Drawing.Point cursor)
    {
        var hit = PInvoke.WindowFromPoint(new System.Drawing.Point(cursor.X, cursor.Y));
        if (hit.IsNull)
        {
            return false;
        }

        var root = PInvoke.GetAncestor(hit, GET_ANCESTOR_FLAGS.GA_ROOT);
        Span<char> name = stackalloc char[32];
        var length = PInvoke.GetClassName(root, name);
        var cls = name[..length];
        return cls.SequenceEqual("Progman") || cls.SequenceEqual("WorkerW");
    }

    private static System.Drawing.Point? ToClient(IInputTarget target, System.Drawing.Point screen)
    {
        var render = target.RenderWindow;
        if (render == 0)
        {
            return null;
        }

        var point = screen;
        return PInvoke.ScreenToClient((HWND)render, ref point) ? point : null;
    }

    private static void Post(IInputTarget target, uint message, nuint wParam, System.Drawing.Point screen)
    {
        if (ToClient(target, screen) is not { } c)
        {
            return;
        }

        var render = target.RenderWindow;
        var lParam = (nint)(((c.Y & 0xFFFF) << 16) | (c.X & 0xFFFF));
        PInvoke.PostMessage((HWND)render, message, (WPARAM)wParam, (LPARAM)lParam);
    }

    public void Dispose()
    {
        if (_hwnd.IsNull)
        {
            return;
        }

        if (_registered)
        {
            var remove = new RawInputDevice { UsagePage = 1, Usage = 2, Flags = RIDEV_REMOVE, Target = default };
            RegisterRawInputDevices(&remove, 1, (uint)sizeof(RawInputDevice));
            _registered = false;
        }

        if (ReferenceEquals(_current, this))
        {
            _current = null;
        }

        PInvoke.DestroyWindow(_hwnd);
        _hwnd = HWND.Null;
    }

    /* ---- Raw Input P/Invoke -------------------------------------------------
       Hand-written, unlike the rest of this project, because RAWINPUT is a variable-sized
       buffer that CsWin32 only exposes through unsafe pointer overloads; the layout is parsed
       by offset above. Everything else in this class goes through CsWin32. */

    private const uint RID_INPUT = 0x10000003;
    private const uint RIDEV_REMOVE = 0x00000001;
    private const uint RIDEV_INPUTSINK = 0x00000100;
    private const uint HeaderSize = 24; // sizeof(RAWINPUTHEADER) on x64

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public HWND Target;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterRawInputDevices(RawInputDevice* devices, uint count, uint size);

    [DllImport("user32.dll")]
    private static extern uint GetRawInputData(nint hRawInput, uint command, byte* data, ref uint size, uint headerSize);
}
