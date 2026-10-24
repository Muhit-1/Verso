using System.Globalization;
using System.IO;
using System.Text;
using Verso.Core;

namespace Verso.App.Hosting;

/// <summary>
/// The application log: one small text file per day in <c>%APPDATA%\Verso\logs</c>, the last few days kept.
/// </summary>
/// <remarks>
/// Always on, unlike <see cref="StartupLog"/> (a timing trace you switch on). It exists so "it stopped working" has
/// something to look at: every place that used to swallow an exception now says what it swallowed.
/// Logging must never be the thing that breaks the app, so every failure in here is dropped.
/// </remarks>
internal static class Log
{
    private const int KeepDays = 7;
    private const long MaxFileBytes = 2 * 1024 * 1024;

    private static readonly Lock Gate = new();
    private static bool _pruned;

    public static void Info(string message) => Write("INFO ", message, null);

    public static void Warn(string message, Exception? ex = null) => Write("WARN ", message, ex);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    /// <summary>Where today's log goes.</summary>
    public static string CurrentFile
        => Path.Combine(VersoPaths.LogDirectory, $"verso-{DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.log");

    /// <summary>Formats one line. Separate from <see cref="Write"/> so it can be tested without touching the disk.</summary>
    internal static string Format(DateTime now, string level, string message, Exception? ex)
    {
        var line = new StringBuilder()
            .Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
            .Append(' ').Append(level).Append(' ').Append(message);

        if (ex is not null)
        {
            line.Append(" | ").Append(ex.GetType().Name).Append(": ").Append(ex.Message);
            var inner = ex.InnerException;
            for (var depth = 0; inner is not null && depth < 3; depth++, inner = inner.InnerException)
            {
                line.Append(" <- ").Append(inner.GetType().Name).Append(": ").Append(inner.Message);
            }

            if (ex.StackTrace is { } stack)
            {
                line.AppendLine().Append(stack);
            }
        }

        return line.ToString();
    }

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            var text = Format(DateTime.Now, level, message, ex) + Environment.NewLine;
            lock (Gate)
            {
                Directory.CreateDirectory(VersoPaths.LogDirectory);
                var file = CurrentFile;

                if (File.Exists(file) && new FileInfo(file).Length > MaxFileBytes)
                {
                    // a runaway loop must not fill the disk; start over instead of growing without bound
                    File.Move(file, file + ".old", overwrite: true);
                }

                File.AppendAllText(file, text);

                if (!_pruned)
                {
                    _pruned = true;
                    Prune();
                }
            }
        }
        catch (Exception)
        {
            // see the class remarks
        }
    }

    private static void Prune()
    {
        var cutoff = DateTime.Now.AddDays(-KeepDays);
        foreach (var file in Directory.EnumerateFiles(VersoPaths.LogDirectory, "verso-*.log*"))
        {
            if (File.GetLastWriteTime(file) < cutoff)
            {
                File.Delete(file);
            }
        }
    }
}
