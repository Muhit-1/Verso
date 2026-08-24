namespace Verso.Core;

/// <summary>
/// Every path Verso reads or writes at runtime. Nothing else in the codebase
/// may build a path by hand.
/// </summary>
/// <remarks>
/// User state lives in %APPDATA%\Verso, never next to the executable — an
/// installed copy under Program Files is not writable.
/// </remarks>
public static class VersoPaths
{
    /// <summary>%APPDATA%\Verso</summary>
    public static string DataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Verso");

    /// <summary>%APPDATA%\Verso\settings.json</summary>
    public static string SettingsFile => Path.Combine(DataRoot, "settings.json");

    /// <summary>%APPDATA%\Verso\tasks.json</summary>
    public static string TasksFile => Path.Combine(DataRoot, "tasks.json");

    /// <summary>%APPDATA%\Verso\logs</summary>
    public static string LogDirectory => Path.Combine(DataRoot, "logs");

    /// <summary>WebView2's own scratch space. Keep it out of the settings folder.</summary>
    public static string WebViewUserData => Path.Combine(DataRoot, "webview2");

    /// <summary>Themes and widgets the user installed themselves.</summary>
    public static string UserThemes => Path.Combine(DataRoot, "themes");

    /// <summary>Themes and widgets the user installed themselves.</summary>
    public static string UserWidgets => Path.Combine(DataRoot, "widgets");

    /// <summary>The web layer that ships with the app (…\app\web).</summary>
    public static string BundledWeb => Path.Combine(AppContext.BaseDirectory, "web");

    /// <summary>Themes that ship with the app.</summary>
    public static string BundledThemes => Path.Combine(BundledWeb, "themes");

    /// <summary>Widgets that ship with the app.</summary>
    public static string BundledWidgets => Path.Combine(BundledWeb, "widgets");

    /// <summary>Creates every writable directory. Safe to call repeatedly.</summary>
    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(WebViewUserData);
        Directory.CreateDirectory(UserThemes);
        Directory.CreateDirectory(UserWidgets);
    }
}
