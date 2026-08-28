using System.IO;
using System.Linq;
using System.Windows;
using Verso.App.Hosting;
using Verso.App.Storage;
using Verso.Core.Models;

namespace Verso.App.Windows;

/// <summary>
/// A real settings window, not just a tray menu. Everything here calls the
/// same <see cref="DesktopActions"/> the tray uses — this window is a
/// second way to reach them, not a second implementation of them.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsStore _settings;
    private readonly DeskController _desk;
    private readonly DesktopActions _actions;
    private readonly ThemeCatalog _themes;

    /// <summary>True while the controls are being populated from state, so setting them doesn't re-fire as a user edit.</summary>
    private bool _loading;

    internal SettingsWindow(SettingsStore settings, DeskController desk, DesktopActions actions, ThemeCatalog themes)
    {
        _settings = settings;
        _desk = desk;
        _actions = actions;
        _themes = themes;

        InitializeComponent();

        Loaded += (_, _) => Refresh();
        Activated += (_, _) => Refresh();
    }

    /// <summary>Re-reads everything from disk/the desk. Cheap, and settings can change from the tray while this window is open.</summary>
    private void Refresh()
    {
        _loading = true;

        PauseButton.Content = _desk.IsShown ? "Hide the desk" : "Show the desk";
        StatusText.Text = _desk.Describe();

        var background = _settings.Current.Background;
        BackgroundText.Text = background.Kind == "none"
            ? "None set — the theme's own backdrop shows."
            : $"{background.Kind}: {Path.GetFileName(background.Source)}";

        var cleanup = _settings.Current.DesktopCleanup;
        HideTaskbarCheck.IsChecked = cleanup.HideTaskbar;
        HideIconsCheck.IsChecked = cleanup.HideDesktopIcons;

        var themeList = _themes.Scan();
        ThemeCombo.ItemsSource = themeList;
        ThemeCombo.SelectedItem = themeList.FirstOrDefault(t => t.Id == _settings.Current.ThemeId);

        _loading = false;
    }

    private void OnTogglePause(object sender, RoutedEventArgs e)
    {
        _desk.SetUserPaused(_desk.IsShown);
        Refresh();
    }

    private void OnReload(object sender, RoutedEventArgs e) => _desk.ReloadWeb();

    private void OnPickBackground(object sender, RoutedEventArgs e)
    {
        _actions.PickBackground(this);
        Refresh();
    }

    private void OnClearBackground(object sender, RoutedEventArgs e)
    {
        _actions.ClearBackground();
        Refresh();
    }

    private void OnHideTaskbarChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _actions.SetTaskbarHidden(HideTaskbarCheck.IsChecked == true);
    }

    private void OnHideIconsChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _actions.SetDesktopIconsHidden(HideIconsCheck.IsChecked == true);
    }

    private void OnThemeChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        if (ThemeCombo.SelectedItem is ThemeManifest theme)
        {
            _actions.SetTheme(theme.Id);
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
