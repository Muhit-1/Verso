using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Verso.Interop;

namespace Verso.Spike.WorkerW;

/// <summary>
/// Phase 0: paint the desktop red from behind the icons.
/// </summary>
/// <remarks>
/// Nothing here is meant to survive into the real app except the call to
/// <see cref="DesktopLayer.Attach"/>. It is a gate, not a foundation.
///
/// The verdict goes to the console as well as the window, because the one
/// question this tool answers — which of Explorer's three desktop layouts does
/// this machine have — is the thing you will want to paste into a bug report a
/// year from now when a Windows update changes it.
/// </remarks>
internal static class Program
{
    [STAThread]
    private static int Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };

        var readout = new TextBlock
        {
            FontFamily = new FontFamily("Consolas, Cascadia Mono, monospace"),
            FontSize = 15,
            Foreground = Brushes.White,
            Margin = new Thickness(48),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            LineHeight = 24,
            Text = "attaching…"
        };

        var window = new Window
        {
            Title = "Verso — Phase 0",
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            AllowsTransparency = false,
            Background = new SolidColorBrush(Color.FromRgb(0xC0, 0x1B, 0x1B)),
            Content = readout,

            // Cover the primary display. Real multi-monitor placement is Phase 4;
            // this only has to prove the layer works.
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = 0,
            Top = 0,
            Width = SystemParameters.PrimaryScreenWidth,
            Height = SystemParameters.PrimaryScreenHeight
        };

        // Esc quits. Without this the only way out is Task Manager, because a
        // window in the desktop layer has no taskbar entry and cannot be focused.
        window.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                window.Close();
            }
        };

        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            var r = DesktopLayer.Attach(hwnd);

            var verdict = Verdict(r);
            var lines = new[]
            {
                "VERSO — PHASE 0",
                "",
                $"attached   {r.Success}",
                $"strategy   {r.Strategy}",
                $"self       0x{hwnd:X}",
                $"parent     0x{r.Parent:X}  ({DesktopLayer.GetClassName(r.Parent)})",
                $"behind     {r.BehindIcons}",
                $"desktop    {r.Diagnostics}",
                "",
                verdict,
                "",
                "Esc to quit."
            };

            readout.Text = string.Join(Environment.NewLine, lines);
            foreach (var line in lines)
            {
                Console.WriteLine(line);
            }
        };

        app.Run(window);
        return 0;
    }

    private static string Verdict(DesktopLayer.AttachOutcome r) => r switch
    {
        { Success: false } =>
            "FAIL — could not reach the desktop layer at all. Is Explorer running?",

        // Attached, but something is in front of us. Usually means Explorer used
        // a layout none of the three strategies recognised.
        { BehindIcons: false } =>
            "PARTIAL — attached, but the desktop icons are NOT in front of this "
            + "window. Report the 'desktop' line above.",

        { Strategy: DesktopLayer.AttachStrategy.Progman } =>
            "PASS (fallback) — attached to Progman itself. Works, but no WorkerW "
            + "was found, so re-check after Windows feature updates.",

        _ =>
            "PASS — if your desktop icons are drawn on top of this red, Phase 1 "
            + "can start."
    };
}
