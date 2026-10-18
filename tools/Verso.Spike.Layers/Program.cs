using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace Verso.Spike.Layers;

/// <summary>
/// Places a solid magenta window in several different ways, and measures from
/// the composed screen — not from what Win32 reports — whether it is visible,
/// whether it covers the desktop icons, and who would receive a click.
/// </summary>
/// <remarks>
/// Every Win32 call on the WorkerW saga reported success while drawing nothing
/// (docs/desktop-layer.md), so this tool trusts only pixels and hit-tests.
///
/// All windows are minimised once up front (Shell "Show desktop") so the
/// desktop is what the screen capture sees, and restored in a finally block.
/// Nothing in Explorer is modified unless --hide-workerw is passed, and then
/// only for the duration of one variant.
/// </remarks>
internal static partial class Program
{
    private const uint Magenta = 0xFF00FF; // COLORREF, same as RGB(255,0,255)

    [STAThread]
    private static int Main(string[] args)
    {
        var outDir = ArgValue(args, "--out") ?? Path.Combine(Environment.CurrentDirectory, "spike-out");
        var hideWorkerW = args.Contains("--hide-workerw");
        Directory.CreateDirectory(outDir);

        // --web: Step 1b. WebView2 + video in the winning placement, then the
        // survival tests. See Program.Web.cs.
        if (args.Contains("--web"))
        {
            return RunWebMode(outDir, ArgValue(args, "--video"), args.Contains("--skip-explorer"), args.Contains("--skip-display"), args.Contains("--no-split-msg"));
        }

        var progman = Native.FindWindowW("Progman", null);
        if (progman == 0)
        {
            Console.WriteLine("Progman not found - Explorer is not running.");
            return 1;
        }

        Console.WriteLine($"Windows {Environment.OSVersion.Version}");
        Console.WriteLine("--- Progman children, front to back ---");
        foreach (var c in Children(progman))
        {
            Console.WriteLine("  " + Describe(c));
        }

        var defView = Native.FindWindowExW(progman, 0, "SHELLDLL_DefView", null);
        var workerW = FindIconlessWorkerW(progman);
        Console.WriteLine($"DefView=0x{defView:X}  Progman-child-WorkerW(no icons)=0x{workerW:X}");
        Console.WriteLine();

        Native.RegisterSpikeClass(Magenta);

        var variants = new List<Variant>
        {
            new("A  top-level, HWND_BOTTOM, click-through (what Verso does today)", Place.TopLevelBottom),
            new("B1 child of Progman, bottom of Progman's z-order", Place.ProgmanBottom),
            new("B2 child of Progman, directly behind DefView", Place.ProgmanBehindDefView),
            new("B3 as B2 + WS_EX_LAYERED", Place.ProgmanBehindDefViewLayered),
            new("B4 as B2 + WS_EX_NOREDIRECTIONBITMAP", Place.ProgmanBehindDefViewNoRedir),
            new("B5 child of Progman, bottom, WS_EX_LAYERED", Place.ProgmanBottomLayered),
            new("C  child of Progman's WorkerW (known-bad control)", Place.InsideWorkerW),
        };
        if (hideWorkerW)
        {
            variants.Add(new("D  as B2, with Progman's WorkerW hidden meanwhile", Place.ProgmanBehindDefViewHideWorkerW));
        }

        var results = new List<string>();
        dynamic? shell = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
            shell!.MinimizeAll();
            Pump(1800);

            var index = 0;
            foreach (var v in variants)
            {
                index++;
                results.Add(Run(v, index, progman, defView, workerW, outDir));
            }
        }
        finally
        {
            try { shell?.UndoMinimizeALL(); } catch { /* nothing more to do */ }
        }

        Console.WriteLine();
        Console.WriteLine("=== RESULTS ===");
        foreach (var r in results)
        {
            Console.WriteLine(r);
        }

        File.WriteAllLines(Path.Combine(outDir, "results.txt"), results);
        return 0;
    }

    private static string Run(Variant v, int index, nint progman, nint defView, nint workerW, string outDir)
    {
        var (w, h) = (Native.GetSystemMetrics(0), Native.GetSystemMetrics(1));
        nint hwnd = 0;
        var workerWWasVisible = false;
        string verdict;
        try
        {
            if (v.Place == Place.ProgmanBehindDefViewHideWorkerW && workerW != 0)
            {
                workerWWasVisible = Native.IsWindowVisible(workerW);
                Native.ShowWindow(workerW, 0 /* SW_HIDE */);
            }

            hwnd = Create(v.Place, progman, workerW, w, h);
            if (hwnd == 0)
            {
                return $"{v.Name}\n    -> could not create window (error {Marshal.GetLastWin32Error()})";
            }

            Position(v.Place, hwnd, defView);
            Native.ShowWindow(hwnd, 8 /* SW_SHOWNA */);
            Position(v.Place, hwnd, defView); // again: showing can reorder
            Pump(1200);

            using var shot = Capture(w, h);
            shot.Save(Path.Combine(outDir, $"{index}.png"), ImageFormat.Png);

            var centre = ToRgb(shot.GetPixel(w / 2, h / 2));
            var centreMagenta = IsMagenta(shot.GetPixel(w / 2, h / 2));

            // The "This PC" and "Recycle Bin" icons sit top-left of the desktop.
            var iconRegions = new[] { new Rectangle(12, 8, 52, 68), new Rectangle(12, 108, 52, 68) };
            var nonMagenta = 0;
            var total = 0;
            foreach (var r in iconRegions)
            {
                for (var y = r.Top; y < r.Bottom; y += 2)
                {
                    for (var x = r.Left; x < r.Right; x += 2)
                    {
                        total++;
                        if (!IsMagenta(shot.GetPixel(x, y)))
                        {
                            nonMagenta++;
                        }
                    }
                }
            }

            var iconFraction = total == 0 ? 0 : (double)nonMagenta / total;
            var hitIcon = HitDescription(38, 40);
            var hitCentre = HitDescription(w / 2, h / 2);

            verdict = !centreMagenta
                ? "INVISIBLE  (centre pixel is not the test colour)"
                : iconFraction > 0.03
                    ? "VISIBLE, icons drawn on top  <-- what we want"
                    : "VISIBLE, but COVERS the icons";

            return $"{v.Name}\n    -> {verdict}\n       centre pixel {centre}; non-magenta in icon area {iconFraction:P0}\n"
                 + $"       click at icon -> {hitIcon}\n       click at centre -> {hitCentre}\n       our window: {Describe(hwnd)}";
        }
        finally
        {
            if (hwnd != 0)
            {
                Native.DestroyWindow(hwnd);
            }

            if (workerWWasVisible)
            {
                Native.ShowWindow(workerW, 8);
            }

            Pump(300);
        }
    }

    private enum Place
    {
        TopLevelBottom,
        ProgmanBottom,
        ProgmanBehindDefView,
        ProgmanBehindDefViewLayered,
        ProgmanBehindDefViewNoRedir,
        ProgmanBottomLayered,
        InsideWorkerW,
        ProgmanBehindDefViewHideWorkerW
    }

    private sealed record Variant(string Name, Place Place);

    private static nint Create(Place place, nint progman, nint workerW, int w, int h)
    {
        const uint WS_POPUP = 0x80000000, WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_CLIPSIBLINGS = 0x04000000;
        const uint EX_LAYERED = 0x80000, EX_TRANSPARENT = 0x20, EX_TOOLWINDOW = 0x80, EX_NOACTIVATE = 0x08000000, EX_NOREDIR = 0x00200000;

        uint ex = 0;
        uint style = WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS;
        nint parent = progman;
        int width = w, height = h;

        switch (place)
        {
            case Place.TopLevelBottom:
                style = WS_POPUP;
                parent = 0;
                ex = EX_LAYERED | EX_TRANSPARENT | EX_TOOLWINDOW | EX_NOACTIVATE;
                break;
            case Place.ProgmanBehindDefViewLayered:
            case Place.ProgmanBottomLayered:
                ex = EX_LAYERED;
                break;
            case Place.ProgmanBehindDefViewNoRedir:
                ex = EX_NOREDIR;
                break;
            case Place.InsideWorkerW:
                parent = workerW != 0 ? workerW : progman;
                break;
        }

        var hwnd = Native.CreateWindowExW(ex, Native.SpikeClass, "Verso layer spike", style, 0, 0, width, height, parent, 0, Native.GetModuleHandleW(null), 0);
        if (hwnd != 0 && (ex & EX_LAYERED) != 0)
        {
            Native.SetLayeredWindowAttributes(hwnd, 0, 255, 0x2 /* LWA_ALPHA */);
        }

        return hwnd;
    }

    private static void Position(Place place, nint hwnd, nint defView)
    {
        const uint FLAGS = 0x0001 | 0x0002 | 0x0010; // NOSIZE | NOMOVE | NOACTIVATE
        nint insertAfter = place switch
        {
            Place.TopLevelBottom or Place.ProgmanBottom or Place.ProgmanBottomLayered or Place.InsideWorkerW => 1, // HWND_BOTTOM
            _ => defView != 0 ? defView : 1
        };

        if (place == Place.TopLevelBottom)
        {
            Native.SetWindowPos(hwnd, 0, 0, 0, Native.GetSystemMetrics(0), Native.GetSystemMetrics(1), 0x0010 | 0x0004);
        }

        Native.SetWindowPos(hwnd, insertAfter, 0, 0, 0, 0, FLAGS);
    }

    /* ---- helpers ---- */

    private static Bitmap Capture(int w, int h)
    {
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(0, 0, 0, 0, new Size(w, h));
        return bmp;
    }

    private static bool IsMagenta(Color c) => c.R > 245 && c.G < 10 && c.B > 245;

    private static string ToRgb(Color c) => $"rgb({c.R},{c.G},{c.B})";

    private static string HitDescription(int x, int y)
    {
        var hit = Native.WindowFromPoint(new Native.POINT { X = x, Y = y });
        var root = Native.GetAncestor(hit, 2 /* GA_ROOT */);
        return $"{Native.ClassOf(hit)} (root {Native.ClassOf(root)})";
    }

    private static string Describe(nint h)
    {
        Native.GetWindowRect(h, out var r);
        var ex = Native.GetWindowLongPtrW(h, -20);
        return $"0x{h:X} {Native.ClassOf(h)} vis={Native.IsWindowVisible(h)} {r.Right - r.Left}x{r.Bottom - r.Top}@{r.Left},{r.Top} ex=0x{ex:X}";
    }

    private static List<nint> Children(nint parent)
    {
        var list = new List<nint>();
        for (var c = Native.FindWindowExW(parent, 0, null, null); c != 0; c = Native.FindWindowExW(parent, c, null, null))
        {
            list.Add(c);
        }

        return list;
    }

    /// <summary>Progman's WorkerW child that is not the icon layer.</summary>
    private static nint FindIconlessWorkerW(nint progman)
    {
        for (var c = Native.FindWindowExW(progman, 0, "WorkerW", null); c != 0; c = Native.FindWindowExW(progman, c, "WorkerW", null))
        {
            if (Native.FindWindowExW(c, 0, "SHELLDLL_DefView", null) == 0)
            {
                return c;
            }
        }

        return 0;
    }

    private static void Pump(int ms)
    {
        var end = Environment.TickCount64 + ms;
        while (Environment.TickCount64 < end)
        {
            while (Native.PeekMessageW(out var msg, 0, 0, 0, 1))
            {
                Native.TranslateMessage(ref msg);
                Native.DispatchMessageW(ref msg);
            }

            Thread.Sleep(15);
        }
    }

    private static string? ArgValue(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}

internal static partial class Native
{
    public const string SpikeClass = "VersoLayerSpike";

    public delegate nint WndProc(nint hwnd, uint msg, nint wParam, nint lParam);

    private static WndProc? _proc; // must outlive the window class

    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    public struct MSG { public nint hwnd; public uint message; public nint wParam; public nint lParam; public uint time; public POINT pt; public uint lPrivate; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize; public uint style; public WndProc lpfnWndProc; public int cbClsExtra; public int cbWndExtra;
        public nint hInstance; public nint hIcon; public nint hCursor; public nint hbrBackground;
        public string? lpszMenuName; public string lpszClassName; public nint hIconSm;
    }

    public static void RegisterSpikeClass(uint colorRef)
    {
        _proc = DefWindowProcW;
        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = _proc,
            hInstance = GetModuleHandleW(null),
            hbrBackground = CreateSolidBrush(colorRef),
            lpszClassName = SpikeClass
        };
        if (RegisterClassExW(ref wc) == 0)
        {
            throw new InvalidOperationException($"RegisterClassEx failed ({Marshal.GetLastWin32Error()})");
        }
    }

    public static string ClassOf(nint h)
    {
        if (h == 0) { return "(none)"; }
        var sb = new StringBuilder(256);
        GetClassNameW(h, sb, sb.Capacity);
        return sb.ToString();
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassExW(ref WNDCLASSEX wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern nint CreateWindowExW(uint ex, string cls, string title, uint style, int x, int y, int w, int h, nint parent, nint menu, nint inst, nint param);
    [DllImport("user32.dll")] private static extern nint DefWindowProcW(nint h, uint m, nint w, nint l);
    [DllImport("user32.dll")] public static extern bool DestroyWindow(nint h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(nint h, int cmd);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(nint h, nint after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint FindWindowW(string? cls, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint FindWindowExW(nint parent, nint after, string? cls, string? title);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(nint h, out RECT r);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(nint h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern nint WindowFromPoint(POINT p);
    [DllImport("user32.dll")] public static extern nint GetAncestor(nint h, uint flags);
    [DllImport("user32.dll")] public static extern nint GetWindowLongPtrW(nint h, int index);
    [DllImport("user32.dll")] public static extern bool SetLayeredWindowAttributes(nint h, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] public static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] public static extern bool PeekMessageW(out MSG m, nint h, uint min, uint max, uint remove);
    [DllImport("user32.dll")] public static extern bool TranslateMessage(ref MSG m);
    [DllImport("user32.dll")] public static extern nint DispatchMessageW(ref MSG m);
    [DllImport("gdi32.dll")] private static extern nint CreateSolidBrush(uint colorRef);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern nint GetModuleHandleW(string? name);
}
