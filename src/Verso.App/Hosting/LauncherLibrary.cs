using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json.Serialization;
using System.Windows;
using Microsoft.Win32;
using Verso.App.Storage;
using Verso.Core.Models;

namespace Verso.App.Hosting;

/// <summary>
/// The app library behind the App launcher: every app the user added, and the boxes they are sorted into.
/// </summary>
/// <remarks>
/// One instance is shared by the desk's bridge and the dashboard's bridge, so both always read and change the
/// same list. Every change raises <see cref="Changed"/>; the desk follows it live, the dashboard re-reads it.
/// "Recent" is built in: a newly added app lands there, and so do the apps of a deleted box.
/// </remarks>
internal sealed class LauncherLibrary(SettingsStore settings)
{
    public const string LauncherWidgetId = "launcher";

    private readonly ConcurrentDictionary<string, string> _icons = new();

    /// <summary>Raised after any change to apps or boxes.</summary>
    public event Action? Changed;

    /// <summary>Raised when a change also switched the launcher widget on (creating a box does that).</summary>
    public event Action? WidgetsChanged;

    public sealed class AppInfo
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
    }

    public sealed class BoxInfo
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("name")] public string Name { get; set; } = "";
        [JsonPropertyName("builtIn")] public bool BuiltIn { get; set; }
        [JsonPropertyName("apps")] public List<AppInfo> Apps { get; set; } = [];
    }

    /// <summary>Recent first (newest app first), then the user's boxes in the order they were made.</summary>
    public List<BoxInfo> Describe()
    {
        var current = settings.Current.Launcher;
        var known = current.Boxes.Select(b => b.Id).ToHashSet();

        // An app whose box was removed some other way is still shown — in Recent.
        string BoxOf(LauncherEntry e) => known.Contains(e.BoxId) ? e.BoxId : LauncherBox.RecentId;
        static AppInfo ToInfo(LauncherEntry e) => new() { Id = e.Id, Name = e.Name };

        var result = new List<BoxInfo>
        {
            new()
            {
                Id = LauncherBox.RecentId,
                Name = "Recent",
                BuiltIn = true,
                Apps = current.Entries.Where(e => BoxOf(e) == LauncherBox.RecentId).Reverse().Select(ToInfo).ToList()
            }
        };

        foreach (var box in current.Boxes)
        {
            result.Add(new BoxInfo
            {
                Id = box.Id,
                Name = box.Name,
                Apps = current.Entries.Where(e => e.BoxId == box.Id).Select(ToInfo).ToList()
            });
        }

        return result;
    }

    // ---- apps ---------------------------------------------------------------

    /// <summary>
    /// Opens a file picker and adds the chosen program. The desk is behind every window, so without its own
    /// top-most owner the dialog could open hidden behind other windows.
    /// </summary>
    public void PickAndAdd(string? boxId, Window? owner)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add an app",
            Filter = "Programs and shortcuts (*.exe;*.lnk)|*.exe;*.lnk",
            Multiselect = true
        };

        Window? temporary = null;
        try
        {
            if (owner is null)
            {
                temporary = new Window
                {
                    Width = 0,
                    Height = 0,
                    WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false,
                    Topmost = true,
                    AllowsTransparency = true,
                    Background = System.Windows.Media.Brushes.Transparent,
                    ShowActivated = true
                };
                temporary.Show();
                temporary.Activate();
            }

            if (dialog.ShowDialog(owner ?? temporary) != true)
            {
                return;
            }
        }
        finally
        {
            temporary?.Close();
        }

        foreach (var file in dialog.FileNames)
        {
            AddApp(file, boxId, raise: false);
        }

        Changed?.Invoke();
    }

    public void AddApp(string path, string? boxId, bool raise = true)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        settings.Update(s =>
        {
            var target = s.Launcher.Boxes.Any(b => b.Id == boxId) ? boxId! : LauncherBox.RecentId;
            s.Launcher.Entries.Add(new LauncherEntry
            {
                Id = "a" + Guid.NewGuid().ToString("N")[..10],
                Name = System.IO.Path.GetFileNameWithoutExtension(path),
                Path = path,
                BoxId = target
            });
        });

        if (raise)
        {
            Changed?.Invoke();
        }
    }

    public void RemoveApp(string id)
    {
        settings.Update(s => s.Launcher.Entries.RemoveAll(e => e.Id == id));
        _icons.TryRemove(id, out _);
        Changed?.Invoke();
    }

    public void MoveApp(string id, string boxId)
    {
        settings.Update(s =>
        {
            var entry = s.Launcher.Entries.Find(e => e.Id == id);
            if (entry is null)
            {
                return;
            }

            entry.BoxId = boxId == LauncherBox.RecentId || s.Launcher.Boxes.Any(b => b.Id == boxId)
                ? boxId
                : LauncherBox.RecentId;
        });
        Changed?.Invoke();
    }

    // ---- boxes --------------------------------------------------------------

    /// <summary>Makes a box and switches the launcher widget on, so the new box is visible at once.</summary>
    public string AddBox()
    {
        var id = "b" + Guid.NewGuid().ToString("N")[..8];
        settings.Update(s =>
        {
            s.Launcher.Boxes.Add(new LauncherBox { Id = id, Name = "New box" });
            s.OutpostWidgets[LauncherWidgetId] = true;
        });
        WidgetsChanged?.Invoke();
        Changed?.Invoke();
        return id;
    }

    public void RenameBox(string id, string name)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0)
        {
            return;
        }

        if (name.Length > 40)
        {
            name = name[..40];
        }

        settings.Update(s =>
        {
            var box = s.Launcher.Boxes.Find(b => b.Id == id);
            if (box is not null)
            {
                box.Name = name;
            }
        });
        Changed?.Invoke();
    }

    /// <summary>Removes a box; its apps are kept and move to Recent.</summary>
    public void DeleteBox(string id)
    {
        settings.Update(s =>
        {
            foreach (var entry in s.Launcher.Entries.Where(e => e.BoxId == id))
            {
                entry.BoxId = LauncherBox.RecentId;
            }

            s.Launcher.Boxes.RemoveAll(b => b.Id == id);
        });
        Changed?.Invoke();
    }

    // ---- icons --------------------------------------------------------------

    /// <returns>A <c>data:image/png</c> URL for the app's icon, or an empty string if it has none.</returns>
    public string GetIcon(string id)
    {
        if (_icons.TryGetValue(id, out var cached))
        {
            return cached;
        }

        var entry = settings.Current.Launcher.Entries.Find(e => e.Id == id);
        var url = entry is null ? "" : ExtractIcon(entry.Path);
        _icons[id] = url;
        return url;
    }

    private static string ExtractIcon(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return "";
            }

            using var icon = Icon.ExtractAssociatedIcon(path);
            if (icon is null)
            {
                return "";
            }

            using var bitmap = icon.ToBitmap();
            using var stream = new MemoryStream();
            bitmap.Save(stream, ImageFormat.Png);
            return "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return "";
        }
    }
}
