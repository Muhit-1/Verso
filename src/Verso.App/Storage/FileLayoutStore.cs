using System.IO;
using Verso.Core;

namespace Verso.App.Storage;

/// <summary>
/// layout.json — where each widget panel sits on the desk.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately stored and returned as an <b>opaque JSON string</b>. The host
/// never parses it, and there is no C# model for it on purpose: what a
/// position means — grid cells, fractions, anchors — is the shell's business,
/// and it will change as the layout system grows. Giving the host an opinion
/// about it would mean a C# change every time the web layer rearranges
/// itself, which is exactly the coupling docs/plan.md exists to prevent.
/// </para>
/// <para>
/// The host's only job here is durability: write it atomically, hand it back
/// unchanged.
/// </para>
/// </remarks>
internal sealed class FileLayoutStore
{
    private readonly Lock _gate = new();

    /// <returns>The stored JSON, or <c>"{}"</c> when there is nothing saved yet.</returns>
    public string Load()
    {
        lock (_gate)
        {
            try
            {
                if (!File.Exists(VersoPaths.LayoutFile))
                {
                    return "{}";
                }

                var text = File.ReadAllText(VersoPaths.LayoutFile).Trim();
                return string.IsNullOrEmpty(text) ? "{}" : text;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A layout we cannot read is a layout the user re-drags in
                // ten seconds. Never a reason to fail to start.
                return "{}";
            }
        }
    }

    public void Save(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(VersoPaths.DataRoot);

                // Temp-then-move, so a crash mid-write costs the change rather
                // than the file. Same rule as JsonFile.
                var temp = VersoPaths.LayoutFile + ".tmp";
                File.WriteAllText(temp, json);
                File.Move(temp, VersoPaths.LayoutFile, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Losing a drag is survivable; throwing into a COM call from
                // script is not.
            }
        }
    }

    /// <summary>Forgets the saved layout, so the desk falls back to its default composition.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            try
            {
                if (File.Exists(VersoPaths.LayoutFile))
                {
                    File.Delete(VersoPaths.LayoutFile);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
