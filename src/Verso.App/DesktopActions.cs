using System.IO;
using System.Windows;
using Microsoft.Win32;
using Verso.App.Storage;
using Verso.Core.Models;
using Verso.Interop;

namespace Verso.App;

/// <summary>
/// The actions behind "Set background…", "Clear background", and the two
/// desktop-cleanup toggles.
/// </summary>
/// <remarks>
/// Shared by the tray menu and <see cref="Windows.DashboardWindow"/> (via
/// <see cref="Hosting.DashboardBridge"/>) so neither duplicates the other's
/// logic — there is exactly one place that
/// knows how to detect a background's kind or apply a cleanup toggle.
/// </remarks>
internal sealed class DesktopActions(SettingsStore settings, DeskController desk, FileLayoutStore layout)
{
    /// <summary>
    /// Forgets where the panels were dragged to and reloads the desk.
    /// </summary>
    /// <remarks>
    /// The escape hatch for a panel dragged somewhere unreachable. The web
    /// layer clamps positions on load so that should not happen, but "put it
    /// back" is a thing a user will look for regardless, and it costs one
    /// method.
    /// </remarks>
    public void ResetLayout()
    {
        layout.Reset();
        desk.ReloadWeb();
    }

    public void PickBackground(Window? owner)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Set Verso background",
            Filter =
                "Supported files (*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.mp4;*.webm;*.html;*.htm)"
                    + "|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.mp4;*.webm;*.html;*.htm"
                + "|Image (*.jpg;*.jpeg;*.png;*.bmp;*.gif)|*.jpg;*.jpeg;*.png;*.bmp;*.gif"
                + "|Video (*.mp4;*.webm)|*.mp4;*.webm"
                + "|Interactive page (*.html;*.htm)|*.html;*.htm"
        };

        var picked = owner is not null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
        if (picked != true)
        {
            return;
        }

        var kind = DetectBackgroundKind(dialog.FileName);
        if (kind == "none")
        {
            MessageBox.Show(owner ?? Application.Current.MainWindow,
                "That file type isn't supported as a background.", "Verso");
            return;
        }

        settings.Update(s =>
        {
            s.Background.Kind = kind;
            s.Background.Source = dialog.FileName;
        });

        if (kind == "image")
        {
            DesktopWallpaper.SetImage(dialog.FileName);
        }

        desk.ReloadWeb();
    }

    public void ClearBackground()
    {
        settings.Update(s =>
        {
            s.Background.Kind = "none";
            s.Background.Source = string.Empty;
        });

        desk.ReloadWeb();
    }

    public void SetTaskbarHidden(bool hide)
    {
        settings.Update(s => s.DesktopCleanup.HideTaskbar = hide);
        DesktopChrome.SetTaskbarVisible(!hide);
    }

    public void SetDesktopIconsHidden(bool hide)
    {
        settings.Update(s => s.DesktopCleanup.HideDesktopIcons = hide);
        DesktopChrome.SetDesktopIconsVisible(!hide);
    }

    public void SetTheme(string themeId)
    {
        if (string.IsNullOrWhiteSpace(themeId))
        {
            return;
        }

        settings.Update(s => s.ThemeId = themeId);
        desk.ReloadWeb();
    }

    public static string DetectBackgroundKind(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" => "image",
        ".mp4" or ".webm" => "video",
        ".html" or ".htm" => "interactive",
        _ => "none"
    };

    /// <summary>
    /// Lets the user add an app to the launcher widget. Picked from a file
    /// dialog, the same shape as <see cref="PickBackground"/> — there is no
    /// way for a widget to add its own entry, only to launch one already
    /// approved this way.
    /// </summary>
    public void AddLauncherEntry(Window? owner)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add an app to the launcher",
            Filter = "Programs and shortcuts (*.exe;*.lnk)|*.exe;*.lnk"
        };

        var picked = owner is not null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
        if (picked != true)
        {
            return;
        }

        settings.Update(s => s.Launcher.Entries.Add(new LauncherEntry
        {
            Id = "a" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Name = Path.GetFileNameWithoutExtension(dialog.FileName),
            Path = dialog.FileName
        }));

        desk.ReloadWeb();
    }

    public void RemoveLauncherEntry(string id)
    {
        settings.Update(s => s.Launcher.Entries.RemoveAll(e => e.Id == id));
        desk.ReloadWeb();
    }

    /// <summary>
    /// Turns a widget on or off. A widget the user turns off is not just
    /// hidden — <see cref="ScriptBridge.GetWidgets"/> stops returning it as
    /// enabled, so the loader never creates an iframe for it and it never
    /// touches the host at all.
    /// </summary>
    public void SetWidgetEnabled(string widgetId, bool enabled)
    {
        settings.Update(s =>
        {
            s.Widgets.Disabled.RemoveAll(id => id == widgetId);
            if (!enabled)
            {
                s.Widgets.Disabled.Add(widgetId);
            }
        });

        desk.ReloadWeb();
    }
}
