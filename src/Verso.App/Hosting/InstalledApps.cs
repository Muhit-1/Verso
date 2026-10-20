using System.IO;
using System.Text.Json.Serialization;

namespace Verso.App.Hosting;

/// <summary>
/// The apps the picker offers: everything in the Start menu (classic programs and Store apps) plus the shortcuts on the
/// Desktop.
/// </summary>
/// <remarks>
/// <para>
/// The Start menu list is read from the shell's <c>shell:AppsFolder</c> — the same list Start itself shows, with no
/// guessing about where programs are installed. Reading it takes a few hundred milliseconds, so it runs on its own STA
/// thread and the result is kept for a couple of minutes.
/// </para>
/// <para>
/// An app is identified by its <em>launch target</em>: a real file path for classic programs and shortcuts, or
/// <c>shell:AppsFolder\&lt;id&gt;</c> for Store apps and anything else only the shell can start. The target is what is
/// stored in the launcher and handed to ShellExecute.
/// </para>
/// </remarks>
internal static class InstalledApps
{
    public sealed class Entry
    {
        /// <summary>What to launch: a file path or a <c>shell:AppsFolder\...</c> address.</summary>
        [JsonPropertyName("target")] public string Target { get; set; } = "";

        [JsonPropertyName("name")] public string Name { get; set; } = "";

        /// <summary>"Start menu", "Store app" or "Desktop".</summary>
        [JsonPropertyName("source")] public string Source { get; set; } = "Start menu";
    }

    private static readonly TimeSpan Fresh = TimeSpan.FromMinutes(2);
    private static readonly string[] LaunchableExtensions = [".exe", ".bat", ".cmd", ".lnk", ".msc"];

    private static List<Entry>? _cache;
    private static DateTime _cachedAt;
    private static Task<List<Entry>>? _running;

    /// <summary>The list, from the cache when it is recent. Never throws; an unreadable list is empty.</summary>
    public static Task<List<Entry>> GetAsync()
    {
        if (_cache is not null && DateTime.UtcNow - _cachedAt < Fresh)
        {
            return Task.FromResult(_cache);
        }

        return _running ??= Task.Run(() =>
        {
            var tcs = new TaskCompletionSource<List<Entry>>();
            var thread = new Thread(() =>
            {
                try
                {
                    tcs.SetResult(Read());
                }
                catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or IOException)
                {
                    tcs.SetResult([]);
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            thread.Start();

            var list = tcs.Task.GetAwaiter().GetResult();
            _cache = list;
            _cachedAt = DateTime.UtcNow;
            _running = null;
            return list;
        });
    }

    private static List<Entry> Read()
    {
        var found = new List<Entry>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var shellType = Type.GetTypeFromProgID("Shell.Application");
        if (shellType is not null)
        {
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic folder = shell.NameSpace("shell:AppsFolder");
            foreach (dynamic item in folder.Items())
            {
                string name = item.Name ?? "";
                string path = item.Path ?? "";
                if (!TryClassify(name, path, out var entry))
                {
                    continue;
                }

                if (names.Add(entry.Name))
                {
                    found.Add(entry);
                }
            }
        }

        // shortcuts on the Desktop (the user's and the shared one)
        foreach (var desktop in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                     Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
                 })
        {
            if (string.IsNullOrEmpty(desktop) || !Directory.Exists(desktop))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(desktop, "*.lnk"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (names.Add(name))
                {
                    found.Add(new Entry { Target = file, Name = name, Source = "Desktop" });
                }
            }
        }

        found.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
        return found;
    }

    /// <summary>
    /// Keeps real, startable apps; drops the documentation pages, web links, folders and uninstallers the Start menu also
    /// lists.
    /// </summary>
    private static bool TryClassify(string name, string path, out Entry entry)
    {
        entry = new Entry();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path)
            || path.Contains("://", StringComparison.Ordinal)
            || name.StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Path.IsPathRooted(path))
        {
            // a file on disk: only things that start a program
            if (!LaunchableExtensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
            {
                return false;
            }

            entry = new Entry { Target = path, Name = name, Source = "Start menu" };
            return true;
        }

        // an app id (Store apps contain '!'; some classic apps register a plain id)
        entry = new Entry
        {
            Target = @"shell:AppsFolder\" + path,
            Name = name,
            Source = path.Contains('!') ? "Store app" : "Start menu"
        };
        return true;
    }
}
