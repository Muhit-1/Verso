using System.IO;
using System.Linq;
using Verso.App.Storage;
using Verso.Core;
using Verso.Core.Models;

namespace Verso.App.Hosting;

/// <summary>
/// Finds every installed widget.
/// </summary>
/// <remarks>
/// Mirrors <see cref="ThemeCatalog"/> exactly — same two roots, same
/// underscore/dot skip rule, same "folder name wins" rule, same
/// user-replaces-bundled override. The one addition is
/// <see cref="WidgetManifest.Source"/>, which the loader needs to know which
/// virtual host to fetch a widget's files from — see
/// <c>web/shell/js/widgets.js</c>.
/// </remarks>
internal sealed class WidgetCatalog
{
    public IReadOnlyList<WidgetManifest> Scan()
    {
        var found = new Dictionary<string, WidgetManifest>(StringComparer.OrdinalIgnoreCase);

        foreach (var (root, source) in new[] { (VersoPaths.BundledWidgets, "bundled"), (VersoPaths.UserWidgets, "user") })
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

                var manifestPath = Path.Combine(folder, "widget.json");
                if (!File.Exists(manifestPath))
                {
                    continue;
                }

                var manifest = JsonFile.Read<WidgetManifest?>(manifestPath, null);
                if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id))
                {
                    continue;
                }

                manifest.Id = name;
                manifest.Source = source;
                found[name] = manifest;
            }
        }

        return found.Values
            .OrderBy(w => w.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Sets <see cref="WidgetManifest.Enabled"/> on each manifest from the
    /// user's disabled-ids list. A free function rather than folded into
    /// <see cref="Scan"/> itself, so scanning stays a pure filesystem read
    /// like <see cref="ThemeCatalog.Scan"/> — both <c>ScriptBridge</c> and
    /// the dashboard need this same computation and would otherwise each
    /// reimplement it.
    /// </summary>
    public static void ApplyEnabled(IEnumerable<WidgetManifest> manifests, IReadOnlyCollection<string> disabledIds)
    {
        var disabled = new HashSet<string>(disabledIds, StringComparer.OrdinalIgnoreCase);
        foreach (var manifest in manifests)
        {
            manifest.Enabled = !disabled.Contains(manifest.Id);
        }
    }
}
