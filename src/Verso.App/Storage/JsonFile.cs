using System.IO;
using System.Text.Json;

namespace Verso.App.Storage;

/// <summary>
/// Reads and writes small JSON files without ever leaving a half-written one
/// behind. Every persisted file in Verso goes through here.
/// </summary>
internal static class JsonFile
{
    internal static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>Extension of the copy kept of the previous good version.</summary>
    internal const string BackupSuffix = ".bak";

    /// <summary>Extension a file that could not be read is renamed to, so it can be looked at later and never overwritten silently.</summary>
    internal const string CorruptSuffix = ".corrupt";

    /// <summary>Reads a file, or returns <paramref name="fallback"/> for anything unreadable.</summary>
    /// <remarks>
    /// A corrupt settings file must not stop the app from starting. Losing a preference is recoverable; a wallpaper
    /// that will not launch is not. But silently starting from defaults would also lose the user's notes and boxes, so
    /// an unreadable file is first set aside as <c>.corrupt</c> and the previous good version (<c>.bak</c>) is tried.
    /// </remarks>
    public static T Read<T>(string path, T fallback)
    {
        if (TryRead(path, out T? value))
        {
            return value ?? fallback;
        }

        if (!File.Exists(path))
        {
            // nothing saved yet is normal, and deleting the file is how a user resets it: do not resurrect the backup
            return fallback;
        }

        Hosting.Log.Warn($"{Path.GetFileName(path)} could not be read; setting it aside and trying the backup");
        SetAside(path);

        if (TryRead(path + BackupSuffix, out value))
        {
            Hosting.Log.Info($"{Path.GetFileName(path)} restored from its backup");
            return value ?? fallback;
        }

        return fallback;
    }

    /// <summary>
    /// Writes to a sibling temp file and then moves it over the target, so a crash mid-write costs you the change
    /// rather than the file. The version being replaced is kept as <c>.bak</c>.
    /// </summary>
    public static void Write<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, Options));
        Backup(path);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Copies the current file to <c>.bak</c>, best effort: a failed backup must not stop the save.</summary>
    internal static void Backup(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Copy(path, path + BackupSuffix, overwrite: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Hosting.Log.Warn($"could not back up {Path.GetFileName(path)}", ex);
        }
    }

    /// <summary>Renames an unreadable file to <c>.corrupt</c> (replacing an older one).</summary>
    internal static void SetAside(string path)
    {
        try
        {
            File.Move(path, path + CorruptSuffix, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Hosting.Log.Warn($"could not set aside {Path.GetFileName(path)}", ex);
        }
    }

    /// <summary>Whether <paramref name="text"/> is a complete JSON document.</summary>
    internal static bool IsValidJson(string text)
    {
        try
        {
            using var _ = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryRead<T>(string path, out T? value)
    {
        value = default;
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            value = JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
            return value is not null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Hosting.Log.Warn($"cannot read {Path.GetFileName(path)}", ex);
            return false;
        }
    }
}
