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
/// <see cref="DesktopLayer.TryAttach"/>. It is a gate, not a foundation.
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
            Left = SystemParameters.VirtualScreenLeft,
            Top = SystemParameters.VirtualScreenTop,
            Width = SystemParameters.PrimaryScreenWidth,
            Height = SystemParameters.PrimaryScreenHeight
        };

        // Esc quits. Without this the only way out is Task Manager, because a
        // window parented into WorkerW has no taskbar entry and cannot be focused.
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
            var result = DesktopLayer.TryAttach(hwnd, out var parent);

            readout.Text = string.Join(Environment.NewLine,
            [
                "VERSO — PHASE 0",
                "",
                $"result   {result}",
                $"self     0x{hwnd:X}",
                $"parent   0x{parent:X}  ({DesktopLayer.GetClassName(parent)})",
                $"desktop  {DesktopLayer.DescribeDesktop()}",
                "",
                result is DesktopLayer.AttachResult.AttachedToWorkerW
                       or DesktopLayer.AttachResult.AttachedToProgman
                    ? "PASS — if your desktop icons are drawn on top of this red, Phase 1 can start."
                    : "FAIL — the WorkerW technique did not work on this build. Read docs/plan.md.",
                "",
                "Esc, or end the process, to quit."
            ]);
        };

        app.Run(window);
        return 0;
    }
}
