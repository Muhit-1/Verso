using System.Collections.Concurrent;
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

        /// <summary>The file behind this app is gone (deleted or moved); launching it cannot work.</summary>
        [JsonPropertyName("missing")] public bool Missing { get; set; }
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
        static AppInfo ToInfo(LauncherEntry e) => new() { Id = e.Id, Name = e.Name, Missing = !Exists(e.Path) };

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

    /// <summary>
    /// Adds the icons currently selected on the real desktop to a box (the user dragged them onto it).
    /// </summary>
    /// <remarks>
    /// A shortcut (.lnk / .url) on the user's own Desktop is <b>moved</b> into Verso's folder: the desktop icon disappears and
    /// the box owns it. Anything else (documents, programs, folders, the shared desktop) is only referenced, never touched.
    /// Undoable with <see cref="Undo"/> for a short while.
    /// </remarks>
    /// <returns><c>found</c> = items with a real file behind them, <c>added</c> = new ones, <c>already</c> = were in the library,
    /// <c>moved</c> = how many shortcuts left the desktop, <c>undo</c> = token for <see cref="Undo"/> (or "").</returns>
    public (int Found, int Added, int Already, int Moved, string Undo) AddDesktopSelection(string boxId)
    {
        var items = DesktopSelection.Read();
        var taken = Targets();
        var desktop = DesktopFolder();
        var moved = new List<LauncherEntry>();
        int added = 0, already = 0;

        foreach (var item in items)
        {
            if (taken.Contains(item.Path))
            {
                already++;
                continue;
            }

            var isShortcut = System.IO.Path.GetExtension(item.Path).ToLowerInvariant() is ".lnk" or ".url";
            var onUserDesktop = string.Equals(System.IO.Path.GetDirectoryName(item.Path), desktop, StringComparison.OrdinalIgnoreCase);
            var entry = AddApp(item.Path, boxId, raise: false, name: item.Name, move: isShortcut && onUserDesktop);
            if (entry is null)
            {
                continue;
            }

            added++;
            if (!string.IsNullOrEmpty(entry.From) && !File.Exists(entry.From))
            {
                moved.Add(entry);   // really left the desktop
            }
        }

        var undo = "";
        if (moved.Count > 0)
        {
            var back = moved.ToList();
            undo = SetUndo(() =>
            {
                foreach (var e in back)
                {
                    RestoreToDesktop(e, out _);
                    RemoveEntryKeepFile(e.Id);
                }
            });
        }

        if (added > 0)
        {
            Changed?.Invoke();
        }

        return (items.Count, added, already, moved.Count, undo);
    }

    // ---- put an app back on the desktop ----------------------------------------------

    /// <summary>
    /// The user dragged a tile out of its box onto the bare desktop. A shortcut Verso holds goes back to the Desktop folder
    /// (the icon reappears); a program or other file gets a new shortcut there. Store apps have no file to place yet. The tile
    /// leaves the box. Undoable with <see cref="Undo"/>.
    /// </summary>
    /// <returns><c>result</c> = "moved" | "created" | "store" | "missing" | "failed"; <c>undo</c> = token (or "").</returns>
    public (string Result, string Name, string Undo) PutOnDesktop(string id)
    {
        var entry = settings.Current.Launcher.Entries.Find(e => e.Id == id);
        if (entry is null)
        {
            return ("missing", "", "");
        }

        if (entry.Path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            return ("store", entry.Name, "");   // nothing to place on the desktop: no file, no icon file
        }

        if (!Exists(entry.Path))
        {
            return ("missing", entry.Name, "");
        }

        var saved = new LauncherEntry { Id = entry.Id, Name = entry.Name, Path = entry.Path, From = entry.From, BoxId = entry.BoxId };
        try
        {
            string result;
            Action undo;
            if (entry.Path.StartsWith(Verso.Core.VersoPaths.Shortcuts, StringComparison.OrdinalIgnoreCase))
            {
                if (!RestoreToDesktop(entry, out var placed))
                {
                    return ("failed", entry.Name, "");
                }

                RemoveEntryKeepFile(id);
                result = "moved";
                undo = () =>
                {
                    File.Move(placed, saved.Path);
                    ReAdd(saved);
                };
            }
            else
            {
                var placed = UniquePath(DesktopFolder(), MakeFileName(entry.Name) + ".lnk");
                CreateShortcut(placed, entry.Path);
                RemoveEntryKeepFile(id);
                result = "created";
                undo = () =>
                {
                    File.Delete(placed);
                    ReAdd(saved);
                };
            }

            var token = SetUndo(undo);
            Changed?.Invoke();
            return (result, saved.Name, token);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            StartupLog.Mark($"put on desktop failed: {entry.Path}: {ex.Message}");
            return ("failed", entry.Name, "");
        }
    }

    // ---- undo (one step, for a short while) ----------------------------------------------

    private sealed record UndoStep(string Token, Action Undo, DateTime Expires);

    private UndoStep? _undo;

    private string SetUndo(Action undo)
    {
        var token = Guid.NewGuid().ToString("N")[..8];
        _undo = new UndoStep(token, undo, DateTime.UtcNow.AddSeconds(30));
        return token;
    }

    /// <summary>Takes back the last move (into a box or out onto the desktop) if the token is the latest and still fresh.</summary>
    public bool Undo(string token)
    {
        var step = _undo;
        if (step is null || step.Token != token || DateTime.UtcNow > step.Expires)
        {
            return false;
        }

        _undo = null;
        try
        {
            step.Undo();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StartupLog.Mark($"undo failed: {ex.Message}");
            return false;
        }

        Changed?.Invoke();
        return true;
    }

    private static string DesktopFolder() => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

    /// <summary>The file name a shortcut had on the desktop (Verso prefixes its copies with s&lt;id&gt;-).</summary>
    private static string RestoreName(LauncherEntry e)
    {
        if (!string.IsNullOrEmpty(e.From))
        {
            return System.IO.Path.GetFileName(e.From);
        }

        var name = System.IO.Path.GetFileName(e.Path);
        return System.Text.RegularExpressions.Regex.Replace(name, "^s[0-9a-f]{8}-", "");
    }

    private static string MakeFileName(string name)
    {
        var bad = System.IO.Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => bad.Contains(c) ? '_' : c).ToArray()).Trim();
        return clean.Length == 0 ? "App" : clean;
    }

    private static string UniquePath(string folder, string fileName)
    {
        var path = System.IO.Path.Combine(folder, fileName);
        for (var n = 2; File.Exists(path); n++)
        {
            path = System.IO.Path.Combine(folder, $"{System.IO.Path.GetFileNameWithoutExtension(fileName)} ({n}){System.IO.Path.GetExtension(fileName)}");
        }

        return path;
    }

    /// <summary>Moves Verso's copy of a shortcut back to the Desktop folder under its old name.</summary>
    private static bool RestoreToDesktop(LauncherEntry entry, out string placed)
    {
        placed = UniquePath(DesktopFolder(), RestoreName(entry));
        try
        {
            File.Move(entry.Path, placed);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void CreateShortcut(string lnkPath, string target)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new System.Runtime.InteropServices.COMException("WScript.Shell is not available");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic link = shell.CreateShortcut(lnkPath);
        link.TargetPath = target;
        var folder = System.IO.Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(folder))
        {
            link.WorkingDirectory = folder;
        }

        link.Save();
    }

    private void RemoveEntryKeepFile(string id)
    {
        settings.Update(s => s.Launcher.Entries.RemoveAll(e => e.Id == id));
        _icons.TryRemove(id, out _);
    }

    private void ReAdd(LauncherEntry entry)
    {
        settings.Update(s =>
        {
            // the box may have been deleted in the meantime: then the app comes back in Recent
            if (entry.BoxId != LauncherBox.RecentId && !s.Launcher.Boxes.Any(b => b.Id == entry.BoxId))
            {
                entry.BoxId = LauncherBox.RecentId;
            }

            s.Launcher.Entries.Add(entry);
        });
    }

    /// <summary>The targets (paths / shell addresses) already in the library, for the picker's "already added" marks.</summary>
    public HashSet<string> Targets() => settings.Current.Launcher.Entries
        .SelectMany(e => string.IsNullOrEmpty(e.From) ? new[] { e.Path } : new[] { e.Path, e.From })
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a stored target is a kind of thing Verso is willing to start: a full path, or a Store/Start-menu app
    /// address. Nothing else — not a web address, a relative path or an arbitrary <c>shell:</c> verb — so an edited
    /// settings file cannot turn a launcher tile into "run whatever this says".
    /// </summary>
    internal static bool IsAllowedTarget(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            return path.StartsWith(@"shell:AppsFolder\", StringComparison.OrdinalIgnoreCase);
        }

        return System.IO.Path.IsPathFullyQualified(path);
    }

    /// <summary>Whether something launchable is still there: a file, a folder, or a shell address (Store apps).</summary>
    public static bool Exists(string path)
        => !string.IsNullOrWhiteSpace(path)
           && (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || File.Exists(path) || Directory.Exists(path));

    /// <summary>
    /// For a shortcut (.lnk / .url) makes Verso's own copy and returns it, so the box does not depend on a file the user
    /// may delete or move. Anything else is returned as it is.
    /// </summary>
    private static string KeepShortcut(string path, out string from, bool move = false)
    {
        from = "";
        var extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (extension is not (".lnk" or ".url") || !File.Exists(path))
        {
            return path;
        }

        var folder = Verso.Core.VersoPaths.Shortcuts;
        if (path.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
        {
            return path;   // already ours
        }

        try
        {
            Directory.CreateDirectory(folder);
            var copy = System.IO.Path.Combine(folder, "s" + Guid.NewGuid().ToString("N")[..8] + "-" + System.IO.Path.GetFileName(path));
            if (move)
            {
                try
                {
                    File.Move(path, copy);   // the desktop icon disappears: it now lives in the box
                    from = path;
                    return copy;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // cannot move (the shared desktop needs admin, a sync client holds the file...): copy it instead
                }
            }

            File.Copy(path, copy, overwrite: false);
            from = path;
            return copy;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return path;   // could not copy: keep pointing at the original rather than fail the add
        }
    }

    /// <summary>Tells the desk and the dashboard that something changed (after a batch of adds with raise: false).</summary>
    public void NotifyChanged() => Changed?.Invoke();

    /// <summary>Raised when something asks for the app picker: the box to add to and, from the desk, where that box is on screen.</summary>
    public event Action<string, System.Drawing.Rectangle?>? PickerRequested;

    public void RequestPicker(string boxId, System.Drawing.Rectangle? anchor) => PickerRequested?.Invoke(boxId, anchor);

    public LauncherEntry? AddApp(string path, string? boxId, bool raise = true, string? name = null, bool move = false)
    {
        if (string.IsNullOrWhiteSpace(path) || Targets().Contains(path))
        {
            return null;
        }

        var stored = KeepShortcut(path, out var from, move);
        LauncherEntry? added = null;
        settings.Update(s =>
        {
            var target = s.Launcher.Boxes.Any(b => b.Id == boxId) ? boxId! : LauncherBox.RecentId;
            added = new LauncherEntry
            {
                Id = "a" + Guid.NewGuid().ToString("N")[..10],
                Name = string.IsNullOrWhiteSpace(name) ? System.IO.Path.GetFileNameWithoutExtension(path) : name,
                Path = stored,
                From = from,
                BoxId = target
            };
            s.Launcher.Entries.Add(added);
        });

        if (raise)
        {
            Changed?.Invoke();
        }

        return added;
    }

    public void RemoveApp(string id)
    {
        var owned = settings.Current.Launcher.Entries.Find(e => e.Id == id)?.Path;
        settings.Update(s => s.Launcher.Entries.RemoveAll(e => e.Id == id));

        // Verso's own copy of a shortcut goes with the entry (never anything else)
        if (owned is not null && owned.StartsWith(Verso.Core.VersoPaths.Shortcuts, StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(owned); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* leave it */ }
        }

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
        var url = entry is null ? "" : ShellIcons.GetDataUrl(entry.Path);
        _icons[id] = url;
        return url;
    }
}
