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

    /// <summary>Reads a file, or returns <paramref name="fallback"/> for anything unreadable.</summary>
    /// <remarks>
    /// A corrupt settings file must not stop the app from starting. Losing a
    /// preference is recoverable; a wallpaper that will not launch is not.
    /// </remarks>
    public static T Read<T>(string path, T fallback)
    {
        try
        {
            if (!File.Exists(path))
            {
                return fallback;
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(json, Options) ?? fallback;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return fallback;
        }
    }

    /// <summary>
    /// Writes to a sibling temp file and then moves it over the target, so a
    /// crash mid-write costs you the change rather than the file.
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
        File.Move(temp, path, overwrite: true);
    }
}
