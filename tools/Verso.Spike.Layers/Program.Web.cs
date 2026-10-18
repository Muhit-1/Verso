using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;

namespace Verso.Spike.Layers;

/// <summary>
/// Step 1b: does a WebView2 playing a real video survive inside the winning
/// placement (layered child of Progman, directly behind the icon layer)?
/// Then the things that break wallpaper apps in practice: Win+D, a display
/// change, and Explorer restarting.
/// </summary>
/// <remarks>
/// Same rule as the first spike: trust pixels, not return values. "Playing" is
/// judged two independent ways - the page reports video.currentTime advancing
/// (decode works), and two screenshots of the composed screen differ (the
/// pixels actually reach the display).
/// </remarks>
internal static partial class Program
{
    private const Place Winner = Place.ProgmanBehindDefViewLayered;

    private static readonly List<string> Report = [];

    private static void L(string line)
    {
        Console.WriteLine(line);
        Report.Add(line);
    }

    private static int RunWebMode(string outDir, string? videoPath, bool skipExplorer, bool skipDisplay, bool noSplit)
    {
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        var context = new ApplicationContext();
        var exit = 0;

        var task = RunWebAsync(outDir, videoPath, skipExplorer, skipDisplay, noSplit);
        task.ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                L("FATAL: " + t.Exception!.GetBaseException());
                exit = 1;
            }

            context.ExitThread();
        }, TaskScheduler.FromCurrentSynchronizationContext());

        Application.Run(context);

        File.WriteAllLines(Path.Combine(outDir, "web-results.txt"), Report);
        return exit;
    }

    /// <summary>
    /// Sends Explorer the undocumented 0x052C and waits for Progman to grow the
    /// extra WorkerW child. Hypothesis under test: that child only exists after
    /// this message, and the winning placement (between DefView and that
    /// WorkerW) is only visible while it does.
    /// </summary>
    private static async Task<string> SendSplitAsync()
    {
        var progman = Native.FindWindowW("Progman", null);
        var before = FindIconlessWorkerW(progman);
        Native.SendMessageTimeoutW(progman, 0x052C, 0xD, 1, 0, 1000, out _);
        var sw = Stopwatch.StartNew();
        nint after = 0;
        while (sw.ElapsedMilliseconds < 3000)
        {
            after = FindIconlessWorkerW(progman);
            if (after != 0) { break; }
            await Task.Delay(100);
        }

        return $"0x052C sent; Progman WorkerW child before=0x{before:X}, after=0x{after:X} ({sw.ElapsedMilliseconds} ms)";
    }

    private static async Task RunWebAsync(string outDir, string? videoPath, bool skipExplorer, bool skipDisplay, bool noSplit)
    {
        var (w, h) = (Native.GetSystemMetrics(0), Native.GetSystemMetrics(1));
        L($"Windows {Environment.OSVersion.Version}, screen {w}x{h}");
        {
            var pm = Native.FindWindowW("Progman", null);
            L($"Progman WorkerW child at start: 0x{FindIconlessWorkerW(pm):X}");
            L(noSplit ? "(--no-split-msg: NOT sending 0x052C)" : await SendSplitAsync());
        }

        var video = videoPath ?? @"C:\Users\senan\Downloads\lich-king.1920x1080.mp4";
        if (!File.Exists(video))
        {
            L($"video not found: {video}");
            return;
        }

        var videoUrl = new Uri(video).AbsoluteUri;
        var staticPage = Path.Combine(outDir, "static.html");
        var videoPage = Path.Combine(outDir, "video.html");
        File.WriteAllText(staticPage, "<!doctype html><body style='margin:0;background:#ff00ff;overflow:hidden'></body>");
        File.WriteAllText(videoPage, VideoPage(videoUrl));

        Native.RegisterSpikeClass(Magenta);
        Native.RegisterCtlClass();
        var ctlWnd = Native.CreateWindowExW(0, Native.CtlClass, "ctl", 0x80000000, 0, 0, 1, 1, 0, 0, Native.GetModuleHandleW(null), 0);

        var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(outDir, "wv2-data"));
        L($"WebView2 runtime {env.BrowserVersionString}");

        dynamic? shell = null;
        Host? host = null;
        var displayChanged = false;
        try
        {
            shell = NewShell();
            shell!.MinimizeAll();
            await Task.Delay(1800);

            /* ---- S0: static magenta page (icons on top?) ---- */
            host = await HostAsync(env, w, h, new Uri(staticPage).AbsoluteUri);
            await Task.Delay(1200);
            using (var shot = Capture(w, h))
            {
                SaveJpeg(shot, Path.Combine(outDir, "web-0-static.jpg"));
                var magentaCentre = IsMagenta(shot.GetPixel(w / 2, h / 2));
                var icons = IconFraction(shot);
                L($"[S0 WebView2 static page in layered Progman child] centre magenta={magentaCentre}, icon-area non-magenta={icons:P0} -> "
                  + (!magentaCentre ? "INVISIBLE" : icons > 0.03 ? "VISIBLE, icons on top (good)" : "VISIBLE but covers icons"));
                L($"    window: {Describe(host.Hwnd)}");
            }

            /* ---- S1: the real video ---- */
            host.Messages.Clear();
            await NavigateAsync(host.Controller, new Uri(videoPage).AbsoluteUri);
            await Task.Delay(3500);
            await JudgeVideo("S1 video", host, w, h, outDir, "web-1-video");

            /* ---- S2: Win+D and back ---- */
            shell!.UndoMinimizeALL();
            await Task.Delay(1000);
            shell!.ToggleDesktop();          // Win+D: show desktop
            await Task.Delay(1500);
            var windowAfterShow = Native.IsWindowVisible(host.Hwnd);
            shell!.ToggleDesktop();          // and back
            await Task.Delay(1500);
            var windowAfterRestore = Native.IsWindowVisible(host.Hwnd);
            shell!.MinimizeAll();
            await Task.Delay(1500);
            L($"[S2 Win+D cycle] window visible after show-desktop={windowAfterShow}, after restore={windowAfterRestore}");
            await JudgeVideo("S2 video after Win+D", host, w, h, outDir, "web-2-afterwind");

            /* ---- S3: display change ---- */
            if (!skipDisplay)
            {
                displayChanged = true;
                var changed = Native.TryChangeResolution(1600, 900);
                L($"[S3 display change] switched to 1600x900: {changed}");
                await Task.Delay(3500);
                var (nw, nh) = (Native.GetSystemMetrics(0), Native.GetSystemMetrics(1));
                var progman = Native.FindWindowW("Progman", null);
                Native.GetWindowRect(progman, out var pr);
                Native.GetWindowRect(host.Hwnd, out var hr);
                L($"    screen now {nw}x{nh}; Progman {pr.Right - pr.Left}x{pr.Bottom - pr.Top}; our window {hr.Right - hr.Left}x{hr.Bottom - hr.Top} (does NOT auto-resize unless equal)");
                using (var shot = Capture(nw, nh))
                {
                    SaveJpeg(shot, Path.Combine(outDir, "web-3-lowres-before-fix.jpg"));
                    L($"    corner luma before fix: bottom-right={Luma(shot.GetPixel(nw - 4, nh - 4)):F0}, top-left={Luma(shot.GetPixel(4, 4)):F0}");
                }

                // what a real host would do on WM_DISPLAYCHANGE
                Native.SetWindowPos(host.Hwnd, 0, 0, 0, nw, nh, 0x0004 | 0x0010);
                host.Controller.Bounds = new Rectangle(0, 0, nw, nh);
                await Task.Delay(1200);
                using (var shot = Capture(nw, nh))
                {
                    SaveJpeg(shot, Path.Combine(outDir, "web-3-lowres-after-fix.jpg"));
                    L($"    after resizing window+webview: bottom-right luma={Luma(shot.GetPixel(nw - 4, nh - 4)):F0}, top-left={Luma(shot.GetPixel(4, 4)):F0}");
                }

                var restored = Native.RestoreResolution();
                displayChanged = false;
                await Task.Delay(3500);
                (nw, nh) = (Native.GetSystemMetrics(0), Native.GetSystemMetrics(1));
                Native.SetWindowPos(host.Hwnd, 0, 0, 0, nw, nh, 0x0004 | 0x0010);
                host.Controller.Bounds = new Rectangle(0, 0, nw, nh);
                await Task.Delay(1200);
                L($"    restored resolution: {restored}; screen {nw}x{nh}");
                await JudgeVideo("S3 video after display round-trip", host, nw, nh, outDir, "web-3-restored");
            }

            /* ---- S4: Explorer restart ---- */
            if (!skipExplorer)
            {
                var before = Native.TaskbarCreatedCount;
                var oldHwnd = host.Hwnd;
                var sw = Stopwatch.StartNew();
                L("[S4 Explorer restart] killing explorer.exe ...");
                foreach (var p in Process.GetProcessesByName("explorer"))
                {
                    try { p.Kill(); } catch { /* already gone */ }
                }

                // Windows restarts the shell by itself; start it ourselves if it does not.
                nint newProgman = 0;
                var startedManually = false;
                for (var i = 0; i < 80; i++)
                {
                    await Task.Delay(250);
                    var pm = Native.FindWindowW("Progman", null);
                    if (pm != 0 && Native.FindWindowExW(pm, 0, "SHELLDLL_DefView", null) != 0)
                    {
                        newProgman = pm;
                        break;
                    }

                    if (i == 24 && Process.GetProcessesByName("explorer").Length == 0)
                    {
                        startedManually = true;
                        Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
                    }
                }

                var shellBackAt = sw.ElapsedMilliseconds;
                L($"    old window still exists={Native.IsWindow(oldHwnd)} (expected False: it was a child of the dead Progman)");
                L($"    shell back after {shellBackAt} ms (started manually={startedManually})");
                if (newProgman == 0)
                {
                    L("    Progman never came back within 20 s - aborting S4");
                }
                else
                {
                    await Task.Delay(3000); // let the taskbar finish and broadcast TaskbarCreated
                    L($"    TaskbarCreated messages received by our top-level window={Native.TaskbarCreatedCount - before}");
                    try { host.Controller.Close(); } catch { /* its window is already gone */ }
                    L($"    Progman children now: {string.Join(", ", ChildClasses(newProgman))}");

                    // 4a: rebuild WITHOUT the split message
                    var rebuild = Stopwatch.StartNew();
                    host = await HostAsync(env, w, h, new Uri(videoPage).AbsoluteUri);
                    L($"    4a rebuilt window + WebView2 in {rebuild.ElapsedMilliseconds} ms (no 0x052C)");
                    shell = NewShell();
                    shell!.MinimizeAll();
                    await Task.Delay(3500);
                    await JudgeVideo("S4a after Explorer restart, no 0x052C", host, w, h, outDir, "web-4a");

                    // 4b: send the split message, rebuild again
                    try { host.Controller.Close(); } catch { /* best effort */ }
                    Native.DestroyWindow(host.Hwnd);
                    L("    " + await SendSplitAsync());
                    L($"    Progman children now: {string.Join(", ", ChildClasses(Native.FindWindowW("Progman", null)))}");
                    rebuild.Restart();
                    host = await HostAsync(env, w, h, new Uri(videoPage).AbsoluteUri);
                    L($"    4b rebuilt window + WebView2 in {rebuild.ElapsedMilliseconds} ms (after 0x052C)");
                    await Task.Delay(3500);
                    await JudgeVideo("S4b after Explorer restart + 0x052C", host, w, h, outDir, "web-4b");
                }
            }
        }
        finally
        {
            if (displayChanged)
            {
                Native.RestoreResolution();
            }

            try { shell?.UndoMinimizeALL(); } catch { /* best effort */ }
            try { host?.Controller.Close(); } catch { /* best effort */ }
            if (host is not null) { Native.DestroyWindow(host.Hwnd); }
            Native.DestroyWindow(ctlWnd);

            if (Process.GetProcessesByName("explorer").Length == 0)
            {
                Process.Start(new ProcessStartInfo("explorer.exe") { UseShellExecute = true });
            }
        }

        L("=== done ===");
    }

    private sealed class Host(nint hwnd, CoreWebView2Controller controller, List<string> messages)
    {
        public nint Hwnd { get; } = hwnd;
        public CoreWebView2Controller Controller { get; } = controller;
        public List<string> Messages { get; } = messages;
    }

    private static async Task<Host> HostAsync(CoreWebView2Environment env, int w, int h, string url)
    {
        var progman = Native.FindWindowW("Progman", null);
        var defView = Native.FindWindowExW(progman, 0, "SHELLDLL_DefView", null);

        var hwnd = Create(Winner, progman, 0, w, h);
        if (hwnd == 0)
        {
            throw new InvalidOperationException($"CreateWindowEx failed ({Marshal.GetLastWin32Error()})");
        }

        Position(Winner, hwnd, defView);
        Native.ShowWindow(hwnd, 8);
        Position(Winner, hwnd, defView);

        var controller = await env.CreateCoreWebView2ControllerAsync(hwnd);
        controller.Bounds = new Rectangle(0, 0, w, h);
        controller.IsVisible = true;
        controller.DefaultBackgroundColor = Color.Black;

        var messages = new List<string>();
        controller.CoreWebView2.WebMessageReceived += (_, e) =>
        {
            var m = e.TryGetWebMessageAsString();
            if (m is not null)
            {
                lock (messages) { messages.Add(m); }
            }
        };

        await NavigateAsync(controller, url);
        return new Host(hwnd, controller, messages);
    }

    private static async Task NavigateAsync(CoreWebView2Controller controller, string url)
    {
        var tcs = new TaskCompletionSource();
        void Done(object? s, CoreWebView2NavigationCompletedEventArgs e) => tcs.TrySetResult();
        controller.CoreWebView2.NavigationCompleted += Done;
        controller.CoreWebView2.Navigate(url);
        await Task.WhenAny(tcs.Task, Task.Delay(15000));
        controller.CoreWebView2.NavigationCompleted -= Done;
    }

    private static async Task JudgeVideo(string name, Host host, int w, int h, string outDir, string file)
    {
        using var a = Capture(w, h);
        await Task.Delay(800);
        using var b = Capture(w, h);
        SaveJpeg(a, Path.Combine(outDir, file + "-a.jpg"));
        SaveJpeg(b, Path.Combine(outDir, file + "-b.jpg"));

        var luma = MeanLuma(a);
        var diff = MeanDiff(a, b);

        double? t1 = null, t2 = null;
        string? last = null;
        lock (host.Messages)
        {
            if (host.Messages.Count > 0) { last = host.Messages[^1]; }
        }

        // the page posts "t=<seconds>;ready=<n>;paused=<bool>" every 250 ms
        if (last is not null)
        {
            t2 = ParseT(last);
        }

        await Task.Delay(1000);
        lock (host.Messages)
        {
            if (host.Messages.Count > 0) { t1 = ParseT(host.Messages[^1]); }
        }

        var decoding = t2 is not null && t1 is not null && t1 != t2;
        var onScreen = luma > 8 && diff > 0.8;
        L($"[{name}] screen mean luma={luma:F0}, frame-to-frame diff={diff:F2}, video.currentTime {t2:F2} -> {t1:F2}; page says '{last}'");
        L($"    -> decoding={decoding}, pixels animating on the real screen={onScreen}  => {(decoding && onScreen ? "PLAYING VISIBLY" : decoding ? "decoding but NOT reaching the screen" : "NOT PLAYING")}");
        L($"    window: {Describe(host.Hwnd)}");
    }

    private static double? ParseT(string msg)
    {
        foreach (var part in msg.Split(';'))
        {
            if (part.StartsWith("t=") && double.TryParse(part[2..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v))
            {
                return v;
            }
        }

        return null;
    }

    private static IEnumerable<string> ChildClasses(nint parent) =>
        Children(parent).Select(c => $"{Native.ClassOf(c)}(vis={Native.IsWindowVisible(c)})");

    private static double Luma(Color c) => (c.R * 299 + c.G * 587 + c.B * 114) / 1000.0;

    private static double MeanLuma(Bitmap b)
    {
        double sum = 0;
        var n = 0;
        for (var y = 0; y < b.Height; y += 24)
        {
            for (var x = 0; x < b.Width; x += 24)
            {
                sum += Luma(b.GetPixel(x, y));
                n++;
            }
        }

        return sum / n;
    }

    private static double MeanDiff(Bitmap a, Bitmap b)
    {
        double sum = 0;
        var n = 0;
        for (var y = 0; y < a.Height; y += 16)
        {
            for (var x = 0; x < a.Width; x += 16)
            {
                var p = a.GetPixel(x, y);
                var q = b.GetPixel(x, y);
                sum += Math.Abs(p.R - q.R) + Math.Abs(p.G - q.G) + Math.Abs(p.B - q.B);
                n++;
            }
        }

        return sum / (3.0 * n);
    }

    private static double IconFraction(Bitmap shot)
    {
        var nonMagenta = 0;
        var total = 0;
        foreach (var r in new[] { new Rectangle(12, 8, 52, 68), new Rectangle(12, 108, 52, 68) })
        {
            for (var y = r.Top; y < r.Bottom; y += 2)
            {
                for (var x = r.Left; x < r.Right; x += 2)
                {
                    total++;
                    if (!IsMagenta(shot.GetPixel(x, y))) { nonMagenta++; }
                }
            }
        }

        return (double)nonMagenta / total;
    }

    private static void SaveJpeg(Bitmap bmp, string path)
    {
        var codec = ImageCodecInfo.GetImageEncoders().First(c => c.MimeType == "image/jpeg");
        using var ps = new EncoderParameters(1);
        ps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 60L);
        bmp.Save(path, codec, ps);
    }

    private static dynamic NewShell() => Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;

    private static string VideoPage(string videoUrl) => $$"""
        <!doctype html>
        <meta charset="utf-8">
        <body style="margin:0;background:#000;overflow:hidden">
        <video id="v" src="{{videoUrl}}" autoplay loop muted playsinline
               style="position:fixed;inset:0;width:100%;height:100%;object-fit:cover"></video>
        <script>
          const v = document.getElementById('v');
          setInterval(() => window.chrome.webview.postMessage(
            't=' + v.currentTime.toFixed(3) + ';ready=' + v.readyState + ';paused=' + v.paused +
            ';err=' + (v.error ? v.error.code : 0)), 250);
        </script>
        """;
}

internal static partial class Native
{
    public const string CtlClass = "VersoLayerSpikeCtl";

    private static WndProc? _ctlProc;
    private static uint _taskbarCreated;
    private static int _taskbarCreatedCount;

    public static int TaskbarCreatedCount => Volatile.Read(ref _taskbarCreatedCount);

    public static void RegisterCtlClass()
    {
        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
        _ctlProc = (h, m, w, l) =>
        {
            if (m == _taskbarCreated)
            {
                Interlocked.Increment(ref _taskbarCreatedCount);
            }

            return DefWindowProcW(h, m, w, l);
        };

        var wc = new CtlClassEx
        {
            cbSize = (uint)Marshal.SizeOf<CtlClassEx>(),
            lpfnWndProc = _ctlProc,
            hInstance = GetModuleHandleW(null),
            lpszClassName = CtlClass
        };
        RegisterCtlClassExW(ref wc);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CtlClassEx
    {
        public uint cbSize; public uint style; public WndProc lpfnWndProc; public int cbClsExtra; public int cbWndExtra;
        public nint hInstance; public nint hIcon; public nint hCursor; public nint hbrBackground;
        public string? lpszMenuName; public string lpszClassName; public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public uint dmFields;
        public int dmPositionX, dmPositionY;
        public uint dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency, dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    /// <summary>Temporary (not written to the registry) switch of the primary display's mode.</summary>
    public static bool TryChangeResolution(int width, int height)
    {
        var current = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
        if (!EnumDisplaySettingsW(null, -1, ref current))
        {
            return false;
        }

        for (var i = 0; ; i++)
        {
            var mode = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
            if (!EnumDisplaySettingsW(null, i, ref mode))
            {
                return false;
            }

            if (mode.dmPelsWidth == width && mode.dmPelsHeight == height
                && mode.dmBitsPerPel == current.dmBitsPerPel && mode.dmDisplayFrequency == current.dmDisplayFrequency)
            {
                mode.dmFields = 0x80000 | 0x100000 | 0x40000 | 0x400000; // W | H | BPP | FREQ
                return ChangeDisplaySettingsW(ref mode, 0) == 0;
            }
        }
    }

    /// <summary>Back to the mode stored in the registry (i.e. the user's real one).</summary>
    public static bool RestoreResolution() => ChangeDisplaySettingsNull(0, 0) == 0;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "RegisterClassExW")] private static extern ushort RegisterCtlClassExW(ref CtlClassEx wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessageW(string name);
    [DllImport("user32.dll")] public static extern bool IsWindow(nint h);
    [DllImport("user32.dll", SetLastError = true)] public static extern nint SendMessageTimeoutW(nint h, uint msg, nint w, nint l, uint flags, uint timeout, out nint result);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplaySettingsW(string? device, int mode, ref DEVMODE dm);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ChangeDisplaySettingsW(ref DEVMODE dm, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "ChangeDisplaySettingsW")] private static extern int ChangeDisplaySettingsNull(nint dm, uint flags);
}
