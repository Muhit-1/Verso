using System.Diagnostics;
using System.IO;
using Verso.Core;

namespace Verso.App.Hosting;

/// <summary>
/// A tiny timeline for "where does the time go?": each mark is written with the milliseconds since the process started.
/// </summary>
/// <remarks>
/// Off unless the environment variable <c>VERSO_TRACE</c> is set to 1; then it appends to <c>%APPDATA%\Verso\trace.log</c>.
/// Costs one boolean check when off. Added because "it feels slow" is not something anyone can fix — a number per step is.
/// </remarks>
internal static class StartupLog
{
    private static readonly bool Enabled = Environment.GetEnvironmentVariable("VERSO_TRACE") == "1";
    private static readonly DateTime Started = Process.GetCurrentProcess().StartTime;
    private static readonly Lock Gate = new();

    public static void Mark(string what)
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            var ms = (int)(DateTime.Now - Started).TotalMilliseconds;
            lock (Gate)
            {
                File.AppendAllText(Path.Combine(VersoPaths.DataRoot, "trace.log"), $"{ms,6} ms  {what}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
            // tracing must never break the app
        }
    }
}
