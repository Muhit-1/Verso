using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Verso.Spike.Layers;

/// <summary>
/// W0 of the widgets plan: can the mouse be mirrored into a page that lives in
/// the wallpaper window — which sits BEHIND the desktop icons and therefore
/// receives no input of its own?
/// </summary>
/// <remarks>
/// <para>
/// Mechanism under test: Raw Input registered with <c>RIDEV_INPUTSINK</c> (a passive
/// listener — no <c>WH_MOUSE_LL</c> hook, nothing is swallowed, Explorer still gets
/// every click), then <c>PostMessage</c> of the equivalent WM_MOUSE* messages to
/// WebView2's <c>Chrome_RenderWidgetHostHWND</c>, only while the cursor is over the
/// bare desktop (root window = Progman/WorkerW).
/// </para>
/// <para>
/// Phase A is automatic and never touches the real cursor: it posts synthetic messages
/// straight to the render window and reads the page's own counters back. That proves the
/// forwarding half. Phase B needs a person: the real mouse must reach the page through
/// Raw Input, which proves the capture half and the "only over the desktop" rule.
/// </para>
/// </remarks>
internal static partial class Program
{
    private static int RunInputMode(string outDir, int seconds, bool noInteractive)
    {
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        var context = new ApplicationContext();
        var exit = 0;

        var task = RunInputAsync(outDir, seconds, noInteractive);
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
        File.WriteAllLines(Path.Combine(outDir, "input-results.txt"), Report);
        return exit;
    }

    /* ---------------- the mirror ---------------- */

    private sealed class Mirror
    {
        public Func<nint> GetRender = () => 0;
        public bool LeftDown;
        public bool WasOver;
        public int Raw, Moves, Downs, Ups, Wheels, Suppressed;
        public string LastOverClass = "";
        private long _lastMoveTick;

        public void OnRaw(nint hRaw)
        {
            uint size = 0;
            Native.GetRawInputData(hRaw, 0x10000003, null, ref size, 24);
            if (size == 0) { return; }
            var buf = new byte[size];
            if (Native.GetRawInputData(hRaw, 0x10000003, buf, ref size, 24) == 0xFFFFFFFF) { return; }
            if (BitConverter.ToUInt32(buf, 0) != 0) { return; } // RIM_TYPEMOUSE

            Raw++;
            var buttonFlags = BitConverter.ToUInt16(buf, 28);
            var buttonData = BitConverter.ToInt16(buf, 30);

            Native.GetCursorPos(out var pt);
            var root = Native.GetAncestor(Native.WindowFromPoint(pt), 2);
            var cls = Native.ClassOf(root);
            LastOverClass = cls;
            var over = cls is "Progman" or "WorkerW";

            var render = GetRender();
            if (render == 0) { return; }

            // A button release must always be delivered if we delivered the press.
            if ((buttonFlags & 0x2) != 0 && LeftDown)
            {
                LeftDown = false;
                Fwd.Button(render, pt, down: false);
                Ups++;
            }

            if (!over)
            {
                if (WasOver) { Fwd.Leave(render); WasOver = false; }
                Suppressed++;
                return;
            }

            WasOver = true;

            if ((buttonFlags & 0x1) != 0)
            {
                LeftDown = true;
                Fwd.Button(render, pt, down: true);
                Downs++;
            }

            if ((buttonFlags & 0x400) != 0)
            {
                Fwd.Wheel(render, pt, buttonData);
                Wheels++;
            }

            var now = Stopwatch.GetTimestamp();
            if (now - _lastMoveTick >= Stopwatch.Frequency / 160) // <= ~160 moves/s
            {
                _lastMoveTick = now;
                Fwd.Move(render, pt, LeftDown);
                Moves++;
            }
        }
    }

    private static class Fwd
    {
        private static nint Pack(int x, int y) => (nint)(((y & 0xFFFF) << 16) | (x & 0xFFFF));

        private static Native.POINT ToClient(nint render, Native.POINT screen)
        {
            var p = screen;
            Native.ScreenToClient(render, ref p);
            return p;
        }

        public static void Move(nint render, Native.POINT screen, bool leftDown)
        {
            var c = ToClient(render, screen);
            Native.PostMessageW(render, 0x0200, leftDown ? 1 : 0, Pack(c.X, c.Y));
        }

        public static void Button(nint render, Native.POINT screen, bool down)
        {
            var c = ToClient(render, screen);
            Native.PostMessageW(render, down ? 0x0201u : 0x0202u, down ? 1 : 0, Pack(c.X, c.Y));
        }

        /// <summary>WM_MOUSEWHEEL: delta in the high word of wParam, SCREEN coordinates in lParam.</summary>
        public static void Wheel(nint render, Native.POINT screen, short delta)
            => Native.PostMessageW(render, 0x020A, (nint)(((uint)(ushort)delta) << 16), Pack(screen.X, screen.Y));

        public static void Leave(nint render) => Native.PostMessageW(render, 0x02A3, 0, 0);
    }

    private static nint FindDescendant(nint parent, string cls)
    {
        for (var c = Native.FindWindowExW(parent, 0, null, null); c != 0; c = Native.FindWindowExW(parent, c, null, null))
        {
            if (Native.ClassOf(c) == cls) { return c; }
            var d = FindDescendant(c, cls);
            if (d != 0) { return d; }
        }

        return 0;
    }

    private static string Tree(nint parent, string indent = "    ")
    {
        var sb = new System.Text.StringBuilder();
        for (var c = Native.FindWindowExW(parent, 0, null, null); c != 0; c = Native.FindWindowExW(parent, c, null, null))
        {
            sb.AppendLine($"{indent}{Describe(c)}");
            sb.Append(Tree(c, indent + "  "));
        }

        return sb.ToString();
    }

    /* ---------------- the run ---------------- */

    private static JsonElement? LatestState(Host host)
    {
        string? last;
        lock (host.Messages) { last = host.Messages.Count > 0 ? host.Messages[^1] : null; }
        if (last is null) { return null; }
        try { return JsonDocument.Parse(last).RootElement.Clone(); } catch (JsonException) { return null; }
    }

    private static int Num(JsonElement? s, string name)
        => s is { } e && e.TryGetProperty(name, out var v) && v.TryGetInt32(out var n) ? n : -1;

    private static (int X, int Y) Centre(JsonElement? s, string id)
    {
        if (s is { } e && e.TryGetProperty("rects", out var r) && r.TryGetProperty(id, out var q))
        {
            return ((int)(q.GetProperty("x").GetDouble() + q.GetProperty("w").GetDouble() / 2),
                    (int)(q.GetProperty("y").GetDouble() + q.GetProperty("h").GetDouble() / 2));
        }

        return (0, 0);
    }

    private static async Task RunInputAsync(string outDir, int seconds, bool noInteractive)
    {
        var (w, h) = (Native.GetSystemMetrics(0), Native.GetSystemMetrics(1));
        L($"Windows {Environment.OSVersion.Version}, screen {w}x{h}");
        L(await SendSplitAsync());

        var pagePath = Path.Combine(outDir, "input-test.html");
        File.WriteAllText(pagePath, InputPage);

        Native.RegisterSpikeClass(Magenta);
        Native.RegisterCtlClass();
        var ctlWnd = Native.CreateWindowExW(0, Native.CtlClass, "ctl", 0x80000000, 0, 0, 1, 1, 0, 0, Native.GetModuleHandleW(null), 0);
        var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(outDir, "wv2-input"));

        dynamic? shell = null;
        Host? host = null;
        var mirror = new Mirror();
        var rawRegistered = false;
        try
        {
            shell = NewShell();
            shell!.MinimizeAll();
            await Task.Delay(1800);

            host = await HostAsync(env, w, h, new Uri(pagePath).AbsoluteUri);
            await Task.Delay(1500);

            nint render = 0;
            mirror.GetRender = () =>
            {
                if (render == 0 || !Native.IsWindow(render))
                {
                    render = FindDescendant(host.Hwnd, "Chrome_RenderWidgetHostHWND");
                }

                return render;
            };

            var r0 = mirror.GetRender();
            L($"WebView2 render window: {(r0 == 0 ? "NOT FOUND" : Describe(r0))}");
            if (r0 == 0)
            {
                L("child window tree:\n" + Tree(host.Hwnd));
                L("cannot continue without a render window");
                return;
            }

            var st = LatestState(host);
            L($"page state received: {st is not null}; element centres: hover={Centre(st, "hover")} btn={Centre(st, "btn")} drag={Centre(st, "drag")} wheel={Centre(st, "wheel")}");

            /* ===== Phase A: synthetic forwarding, real cursor untouched ===== */
            L("--- Phase A: synthetic messages posted to the render window ---");
            var hv = Centre(st, "hover"); var bt = Centre(st, "btn"); var dr = Centre(st, "drag"); var wh = Centre(st, "wheel");
            static Native.POINT P(int x, int y) => new() { X = x, Y = y };
            // Screen == client here: the host window is at 0,0 on the primary monitor.

            // The page reports its counters every 150 ms, so wait longer than that before each read.
            Fwd.Move(r0, P(5, 5), false); await Task.Delay(400);
            var s0 = LatestState(host);
            Fwd.Move(r0, P(hv.X, hv.Y), false); await Task.Delay(400);
            var s1 = LatestState(host);
            Fwd.Move(r0, P(5, 5), false); await Task.Delay(400);
            var s2 = LatestState(host);
            L($"  hover : enters {Num(s0, "enters")} -> {Num(s1, "enters")}, :hover while over = {Hov(s1)}, after leaving = {Hov(s2)}   => {Verdict(Num(s1, "enters") > Num(s0, "enters") && Hov(s1) && !Hov(s2))}");

            var c0 = Num(LatestState(host), "clicks");
            Fwd.Move(r0, P(bt.X, bt.Y), false); await Task.Delay(60);
            Fwd.Button(r0, P(bt.X, bt.Y), true); await Task.Delay(60);
            Fwd.Button(r0, P(bt.X, bt.Y), false); await Task.Delay(200);
            var c1 = Num(LatestState(host), "clicks");
            L($"  click : button clicks {c0} -> {c1}   => {Verdict(c1 == c0 + 1)}");

            Fwd.Move(r0, P(dr.X, dr.Y), false); await Task.Delay(60);
            Fwd.Button(r0, P(dr.X, dr.Y), true); await Task.Delay(60);
            for (var i = 1; i <= 20; i++) { Fwd.Move(r0, P(dr.X + i * 10, dr.Y + i * 4), true); await Task.Delay(16); }
            await Task.Delay(100);
            var dragMid = Num(LatestState(host), "dragMax");
            Fwd.Button(r0, P(dr.X + 200, dr.Y + 80), false); await Task.Delay(250);
            var sd = LatestState(host);
            L($"  drag  : furthest drag distance seen by the page = {Num(sd, "dragMax")} px (moved 215 px); drops = {Num(sd, "drops")}   => {Verdict(Num(sd, "dragMax") >= 180 && Num(sd, "drops") >= 1)}");

            var w0 = Num(LatestState(host), "wheel");
            Fwd.Move(r0, P(wh.X, wh.Y), false); await Task.Delay(60);
            for (var i = 0; i < 3; i++) { Fwd.Wheel(r0, P(wh.X, wh.Y), 120); await Task.Delay(50); }
            await Task.Delay(200);
            await Task.Delay(300);
            var w1 = Num(LatestState(host), "wheel");
            L($"  wheel (posted WM_MOUSEWHEEL): page wheel total {w0} -> {w1} (3 notches sent)   => {Verdict(w1 > w0)}");

            // Fallback: the host injects the wheel through script instead.
            for (var i = 0; i < 3; i++)
            {
                await host.Controller.CoreWebView2.ExecuteScriptAsync($"window.__wheel({wh.X},{wh.Y},120)");
                await Task.Delay(50);
            }

            await Task.Delay(400);
            var w2 = Num(LatestState(host), "wheel");
            L($"  wheel (script fallback)      : page wheel total {w1} -> {w2}   => {Verdict(w2 > w1)}");

            Fwd.Leave(r0);

            /* ===== Phase B: the real mouse, through Raw Input ===== */
            if (noInteractive)
            {
                L("(--no-interactive: skipping Phase B)");
                return;
            }

            L($"--- Phase B: real mouse via Raw Input for {seconds} s (desktop is shown; please move over it, click the button, drag the box, scroll the dashed area) ---");
            Native.RawInputHandler = mirror.OnRaw;
            var dev = new[] { new Native.RAWINPUTDEVICE { UsagePage = 1, Usage = 2, Flags = 0x100 /* RIDEV_INPUTSINK */, Target = ctlWnd } };
            rawRegistered = Native.RegisterRawInputDevices(dev, 1, (uint)Marshal.SizeOf<Native.RAWINPUTDEVICE>());
            L($"  RegisterRawInputDevices(INPUTSINK) = {rawRegistered}");

            var before = LatestState(host);
            var cpu0 = Process.GetCurrentProcess().TotalProcessorTime;
            var t0 = Stopwatch.StartNew();
            for (var left = seconds; left > 0; left--)
            {
                await host.Controller.CoreWebView2.ExecuteScriptAsync(
                    $"document.getElementById('banner').textContent='LIVE TEST ({left}s): move over the desktop, click the big button, drag the box, scroll the dashed area. Then move onto an app window / the taskbar.'");
                await Task.Delay(1000);
            }

            var cpu = (Process.GetCurrentProcess().TotalProcessorTime - cpu0).TotalMilliseconds;
            var after = LatestState(host);
            L($"  raw mouse events received by the listener : {mirror.Raw}");
            L($"  forwarded to page: {mirror.Moves} moves, {mirror.Downs} presses, {mirror.Ups} releases, {mirror.Wheels} wheel; held back (cursor not over the bare desktop): {mirror.Suppressed}");
            L($"  last window under cursor class: {mirror.LastOverClass}");
            L($"  page counters before -> after: moves {Num(before, "moves")} -> {Num(after, "moves")}, enters {Num(before, "enters")} -> {Num(after, "enters")} (leaves {Num(before, "leaves")} -> {Num(after, "leaves")}), clicks {Num(before, "clicks")} -> {Num(after, "clicks")}, dragMax {Num(before, "dragMax")} -> {Num(after, "dragMax")}, wheel {Num(before, "wheel")} -> {Num(after, "wheel")}");
            L($"  spike process CPU during Phase B: {cpu:F0} ms over {t0.Elapsed.TotalSeconds:F0} s = {100 * cpu / t0.Elapsed.TotalMilliseconds:F2}% of one core");
            L(mirror.Raw == 0
                ? "  => no mouse movement was seen: inconclusive (nobody moved the mouse?)"
                : $"  => real mouse reached the page: {(Num(after, "moves") > Num(before, "moves") ? "YES" : "NO")}; click reached the button: {(Num(after, "clicks") > Num(before, "clicks") ? "YES" : "not exercised")}");
        }
        finally
        {
            Native.RawInputHandler = null;
            if (rawRegistered)
            {
                Native.RegisterRawInputDevices([new Native.RAWINPUTDEVICE { UsagePage = 1, Usage = 2, Flags = 0x1 /* RIDEV_REMOVE */, Target = 0 }], 1, (uint)Marshal.SizeOf<Native.RAWINPUTDEVICE>());
            }

            try { shell?.UndoMinimizeALL(); } catch { /* best effort */ }
            try { host?.Controller.Close(); } catch { /* best effort */ }
            if (host is not null) { Native.DestroyWindow(host.Hwnd); }
            Native.DestroyWindow(ctlWnd);
            L("=== done ===");
        }
    }

    private static bool Hov(JsonElement? s) => s is { } e && e.TryGetProperty("hover", out var v) && v.ValueKind == JsonValueKind.True;

    private static string Verdict(bool ok) => ok ? "PASS" : "FAIL";

    private const string InputPage = """
        <!doctype html>
        <meta charset="utf-8">
        <body style="margin:0;background:linear-gradient(135deg,#0b1320,#1d2b45);color:#fff;font:16px system-ui;overflow:hidden;user-select:none">
        <style>
          .b{position:absolute;border:2px solid #ffffff66;border-radius:16px;display:grid;place-items:center;font-size:22px;background:#ffffff14}
          #hover:hover{background:#2ecc71aa}
          #btn{font:inherit;font-size:24px;color:#fff;cursor:pointer}
          #btn:active{background:#e67e22aa}
          #wheel{border-style:dashed}
        </style>
        <div id="banner" style="position:absolute;left:140px;top:30px;right:30px;font-size:22px;color:#ffd166">Waiting…</div>
        <div id="hover" class="b" style="left:300px;top:200px;width:300px;height:160px">hover me</div>
        <button id="btn" class="b" style="left:700px;top:200px;width:300px;height:160px">click me (0)</button>
        <div id="drag" class="b" style="left:300px;top:450px;width:200px;height:120px;background:#3498dbaa;cursor:grab">drag me</div>
        <div id="wheel" class="b" style="left:700px;top:450px;width:300px;height:160px">scroll here (0)</div>
        <div id="pos" style="position:absolute;left:300px;top:650px;font:14px monospace;opacity:.8"></div>
        <script>
          const S = {moves:0, enters:0, leaves:0, clicks:0, dragMax:0, drops:0, wheel:0, hover:false, mx:-1, my:-1};
          const $ = id => document.getElementById(id);
          document.addEventListener('mousemove', e => { S.moves++; S.mx = e.clientX; S.my = e.clientY; });
          $('hover').addEventListener('mouseenter', () => { S.enters++; S.hover = true; });
          $('hover').addEventListener('mouseleave', () => { S.hover = false; S.leaves++; });
          $('btn').addEventListener('click', () => { S.clicks++; $('btn').textContent = 'click me (' + S.clicks + ')'; });
          let d = null; const dg = $('drag');
          dg.addEventListener('pointerdown', e => { d = {x:e.clientX, y:e.clientY, l:dg.offsetLeft, t:dg.offsetTop}; });
          addEventListener('pointermove', e => { if (!d) return; const dx = e.clientX - d.x, dy = e.clientY - d.y;
            S.dragMax = Math.max(S.dragMax, Math.round(Math.hypot(dx, dy))); dg.style.left = (d.l + dx) + 'px'; dg.style.top = (d.t + dy) + 'px'; });
          addEventListener('pointerup', () => { if (d) { S.drops++; d = null; } });
          $('wheel').addEventListener('wheel', e => { S.wheel += Math.abs(e.deltaY); $('wheel').textContent = 'scroll here (' + S.wheel + ')'; });
          // Fallback used when native wheel messages cannot be delivered: the host calls this.
          window.__wheel = (x, y, dy) => { const el = document.elementFromPoint(x, y); if (!el) return;
            const ev = new WheelEvent('wheel', {deltaY: dy, clientX: x, clientY: y, bubbles: true, cancelable: true});
            if (el.dispatchEvent(ev)) { let n = el; while (n && n !== document.body) { if (n.scrollHeight > n.clientHeight) { n.scrollTop += dy; break; } n = n.parentElement; } } };
          function rects() { const r = {}; for (const id of ['hover','btn','drag','wheel']) { const b = $(id).getBoundingClientRect(); r[id] = {x:b.x, y:b.y, w:b.width, h:b.height}; } return r; }
          const home = rects();
          setInterval(() => { $('pos').textContent = 'mouse in page: ' + S.mx + ',' + S.my + '  moves ' + S.moves;
            chrome.webview.postMessage(JSON.stringify({...S, rects: home})); }, 150);
        </script>
        """;
}

internal static partial class Native
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RAWINPUTDEVICE { public ushort UsagePage; public ushort Usage; public uint Flags; public nint Target; }

    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterRawInputDevices([In] RAWINPUTDEVICE[] devices, uint count, uint size);
    [DllImport("user32.dll")] public static extern uint GetRawInputData(nint hRawInput, uint command, [Out] byte[]? data, ref uint size, uint headerSize);
    [DllImport("user32.dll")] public static extern bool PostMessageW(nint h, uint msg, nint w, nint l);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern bool ScreenToClient(nint h, ref POINT p);
}
