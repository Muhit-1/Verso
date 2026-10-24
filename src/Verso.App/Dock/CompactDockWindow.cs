using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Verso.App.Dock;

/// <summary>
/// The compact dock: a small floating pill that is exactly as wide as the open apps' icons. A real top-most
/// window that never takes focus (measured: showing it did not change the foreground window, project notes "S0").
/// </summary>
internal sealed class CompactDockWindow : Window
{
    private readonly DockWindowList _list;
    private readonly Border _pill;
    private readonly StackPanel _row;
    private string _signature = string.Empty;
    private bool _shown;

    public event Action<DockEntry>? EntryClicked;
    public event Action? StartClicked;

    public CompactDockWindow(DockWindowList list)
    {
        _list = list;

        Title = "Verso dock";
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Opacity = 0;
        UseLayoutRounding = true;

        _row = new StackPanel { Orientation = Orientation.Horizontal };
        _pill = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xD0, 0x1A, 0x1A, 0x1E)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x20, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(6),
            Child = _row,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = .35 }
        };
        Content = new Border { Padding = new Thickness(14), Child = _pill };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;
        var ex = (long)NativeDock.GetWindowLongPtr(hwnd, NativeDock.GWL_EXSTYLE);
        NativeDock.SetWindowLongPtr(hwnd, NativeDock.GWL_EXSTYLE, (IntPtr)(ex | NativeDock.WS_EX_NOACTIVATE | NativeDock.WS_EX_TOOLWINDOW));

        // A click on the dock must never activate it (that would take the keyboard from the app you are working in).
        HwndSource.FromHwnd(hwnd)?.AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
        {
            if (msg == NativeDock.WM_MOUSEACTIVATE)
            {
                handled = true;
                return (IntPtr)3; // MA_NOACTIVATE
            }

            return IntPtr.Zero;
        });
    }

    public IntPtr Handle => new WindowInteropHelper(this).Handle;

    /// <summary>Rebuilds the icons when something visible changed, then places the dock on the screen.</summary>
    public void Refresh(List<DockEntry> entries, IntPtr foreground, Verso.Core.Models.DockSettings settings, (int Position, int Align, bool Small) layout)
    {
        var vertical = layout.Position is 0 or 2;
        var signature = string.Join("|", entries.Select(e => $"{e.Key}:{e.AllMinimised}:{e.Windows.Count}:{e.Windows.Contains(foreground)}"))
            + $"#{settings.ShowStart}#{layout.Position}#{layout.Small}";

        if (signature != _signature)
        {
            _signature = signature;
            Rebuild(entries, foreground, settings, layout.Small, vertical);
        }

        Place(layout);
    }

    private void Rebuild(List<DockEntry> entries, IntPtr foreground, Verso.Core.Models.DockSettings settings, bool small, bool vertical)
    {
        var icon = small ? 20 : 26;
        _row.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        _pill.CornerRadius = new CornerRadius(vertical ? 18 : 20);
        _row.Children.Clear();

        if (settings.ShowStart)
        {
            var start = Tile(icon, null, "Start", false, false, 0);
            start.Child = new Border
            {
                Width = icon, Height = icon, CornerRadius = new CornerRadius(5),
                Background = new LinearGradientBrush(Color.FromRgb(0x6A, 0xA8, 0xE8), Color.FromRgb(0x3C, 0x7D, 0xD0), 45)
            };
            start.MouseLeftButtonUp += (_, _) => StartClicked?.Invoke();
            _row.Children.Add(start);
        }

        foreach (var entry in entries)
        {
            var active = entry.Windows.Contains(foreground);
            var tile = Tile(icon, _list.IconFor(entry), entry.Title, active, entry.AllMinimised, entry.Windows.Count);
            var captured = entry;
            tile.MouseLeftButtonUp += (_, _) => EntryClicked?.Invoke(captured);
            _row.Children.Add(tile);
        }
    }

    private static Border Tile(int icon, ImageSource? image, string tip, bool active, bool minimised, int count)
    {
        var size = icon + 14;
        var grid = new Grid { Width = size, Height = size, Cursor = Cursors.Hand, Background = Brushes.Transparent };
        var host = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(10), Background = Brushes.Transparent, ToolTip = tip };

        if (image is not null)
        {
            grid.Children.Add(new Image { Source = image, Width = icon, Height = icon, Opacity = minimised ? .5 : 1, SnapsToDevicePixels = true });
        }
        else
        {
            grid.Children.Add(new Border { Width = icon, Height = icon, CornerRadius = new CornerRadius(6), Background = new SolidColorBrush(Color.FromArgb(0x60, 0xFF, 0xFF, 0xFF)), Opacity = minimised ? .5 : 1 });
        }

        if (active)
        {
            grid.Children.Add(new Border
            {
                Width = 14, Height = 3, CornerRadius = new CornerRadius(2), Background = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 1)
            });
        }

        if (count > 1)
        {
            grid.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(7), Background = new SolidColorBrush(Color.FromRgb(0xB8, 0x5C, 0x38)),
                MinWidth = 14, Height = 14, Padding = new Thickness(3, 0, 3, 0),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 1, 0),
                Child = new TextBlock { Text = count.ToString(), Foreground = Brushes.White, FontSize = 9, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            });
        }

        host.Child = grid;
        host.MouseEnter += (_, _) => host.Background = new SolidColorBrush(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF));
        host.MouseLeave += (_, _) => host.Background = Brushes.Transparent;
        return host;
    }

    /// <summary>Bottom/top: centred or at the left; left/right: stacked, centred or at the top — like the taskbar it replaces.</summary>
    private void Place((int Position, int Align, bool Small) layout)
    {
        Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var w = DesiredSize.Width;
        var h = DesiredSize.Height;
        var screen = SystemParameters.PrimaryScreenWidth;
        var screenH = SystemParameters.PrimaryScreenHeight;
        const double edge = 4;   // the content has its own 14 DIP padding for the shadow

        double left, top;
        switch (layout.Position)
        {
            case 1: // top
                left = layout.Align == 0 ? edge : (screen - w) / 2;
                top = edge;
                break;
            case 0: // left
                left = edge;
                top = layout.Align == 0 ? edge : (screenH - h) / 2;
                break;
            case 2: // right
                left = screen - w - edge;
                top = layout.Align == 0 ? edge : (screenH - h) / 2;
                break;
            default: // bottom
                left = layout.Align == 0 ? edge : (screen - w) / 2;
                top = screenH - h - edge;
                break;
        }

        if (Math.Abs(Left - left) > .5) Left = left;
        if (Math.Abs(Top - top) > .5) Top = top;
    }

    public void SetShown(bool shown)
    {
        if (shown == _shown)
        {
            return;
        }

        _shown = shown;

        if (shown)
        {
            if (!IsVisible)
            {
                Show();
            }

            BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)));
        }
        else
        {
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(140));
            fade.Completed += (_, _) =>
            {
                if (!_shown)
                {
                    Hide();
                }
            };
            BeginAnimation(OpacityProperty, fade);
        }
    }
}
