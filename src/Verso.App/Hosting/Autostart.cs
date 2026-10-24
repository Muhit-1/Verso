using System.IO;
using Microsoft.Win32;

namespace Verso.App.Hosting;

/// <summary>"Start with Windows": one value under the current user's Run key. No admin rights, nothing machine-wide.</summary>
/// <remarks>
/// The registry is the truth, not <c>settings.json</c>: the installer can set the same value, and the user can remove
/// it in Task Manager's Startup tab. The setting only mirrors it.
/// </remarks>
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Verso";

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) is string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.Warn("cannot read the start-with-Windows setting", ex);
            return false;
        }
    }

    public static void Set(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled && Environment.ProcessPath is { } exe)
            {
                key.SetValue(ValueName, $"\"{exe}\"");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            Log.Warn("cannot change the start-with-Windows setting", ex);
        }
    }
}
