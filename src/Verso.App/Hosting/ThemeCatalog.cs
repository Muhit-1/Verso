using System.IO;
using Verso.App.Storage;
using Verso.Core;
using Verso.Core.Models;

namespace Verso.App.Hosting;

/// <summary>
/// Finds every installed theme.
/// </summary>
/// <remarks>
/// <para>
/// A theme is a folder containing a theme.json. Two places are scanned: the
/// bundled root and the user root passed to the constructor. A user theme
/// with the same id as a bundled one replaces it, which is how someone
/// retints a stock theme without editing Program Files.
/// </para>
/// <para>
/// Folders whose names begin with an underscore are skipped — that is how
/// <c>_template</c> stays out of the picker.
/// </para>
/// <para>
/// Takes its roots rather than hardcoding <see cref="VersoPaths.BundledThemes"/>/
/// <see cref="VersoPaths.UserThemes"/> so the same class serves both the desk's
/// themes and the dashboard's — two different token contracts (see
/// docs/theme-spec.md vs docs/dashboard-theme-spec.md), each with its own pair
/// of folders, but identical scan/merge/skip rules.
/// </para>
/// </remarks>
internal sealed class ThemeCatalog(string bundledRoot, string userRoot)
{
    public IReadOnlyList<ThemeManifest> Scan()
    {
        var found = new Dictionary<string, ThemeManifest>(StringComparer.OrdinalIgnoreCase);

        foreach (var (root, source) in new[] { (bundledRoot, "bundled"), (userRoot, "user") })
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var folder in Directory.EnumerateDirectories(root))
            {
                var name = Path.GetFileName(folder);
                if (name.StartsWith('_') || name.StartsWith('.'))
                {
                    continue;
                }

                var manifestPath = Path.Combine(folder, "theme.json");
                if (!File.Exists(manifestPath))
                {
                    continue;
                }

                var manifest = JsonFile.Read<ThemeManifest?>(manifestPath, null);
                if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id))
                {
                    continue;
                }

                // The folder name wins. An id that disagrees with its folder
                // would break every relative URL the shell builds from it.
                manifest.Id = name;
                manifest.Source = source;
                found[name] = manifest;
            }
        }

        return found.Values
            .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
