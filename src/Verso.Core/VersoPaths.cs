namespace Verso.Core;

/// <summary>
/// Every path Verso reads or writes at runtime. Nothing else in the codebase
/// may build a path by hand.
/// </summary>
/// <remarks>
/// <para>
/// User state defaults to %APPDATA%\Verso, never next to the executable — an
/// installed copy under Program Files is not writable.
/// </para>
/// <para>
/// That default can be redirected to another drive, which matters more than it
/// sounds: the WebView2 cache under this root grows without bound, and a full
/// system drive is the common reason someone goes looking for it. See
/// <see cref="ResolveDataRoot"/>.
/// </para>
/// </remarks>
public static class VersoPaths
{
    /// <summary>Overrides the data root outright. Absolute path. Mainly for development.</summary>
    public const string DataRootVariable = "VERSO_DATA";

    /// <summary>
    /// Name of the pointer file that redirects the data root elsewhere.
    /// </summary>
    /// <remarks>
    /// It lives at the <em>default</em> location and contains one line: the
    /// absolute path of the real data folder. A few bytes stay on the system
    /// drive; everything that actually grows lives wherever it points.
    ///
    /// This cannot be a setting in settings.json — settings.json is itself
    /// inside the data root, so reading it would require already knowing the
    /// answer.
    /// </remarks>
    public const string PointerFileName = "data-location.txt";

    /// <summary>The default root, whether or not it is the one in use.</summary>
    public static string DefaultDataRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Verso");

    /// <summary>Where user state actually lives. Resolved once, at startup.</summary>
    public static string DataRoot { get; } = ResolveDataRoot();

    /// <summary>Whether the data root has been redirected away from the default.</summary>
    public static bool IsRedirected
        => !string.Equals(DataRoot, DefaultDataRoot, StringComparison.OrdinalIgnoreCase);

    /// <summary>Full path of the pointer file, at the default location.</summary>
    public static string PointerFile => Path.Combine(DefaultDataRoot, PointerFileName);

    /// <summary>
    /// Environment variable first, then the pointer file, then the default.
    /// </summary>
    /// <remarks>
    /// Never throws and never returns something unusable: an unreadable or
    /// nonsensical pointer falls back to the default rather than stopping the
    /// app from starting. Losing a redirect costs you disk space in the wrong
    /// place; failing to start costs you the wallpaper.
    /// </remarks>
    private static string ResolveDataRoot()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(DataRootVariable);
        if (IsUsable(fromEnvironment))
        {
            return Path.GetFullPath(fromEnvironment!);
        }

        try
        {
            var pointer = Path.Combine(DefaultDataRoot, PointerFileName);
            if (File.Exists(pointer))
            {
                var target = File.ReadAllText(pointer).Trim();
                if (IsUsable(target))
                {
                    return Path.GetFullPath(target);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Fall through to the default.
        }

        return DefaultDataRoot;
    }

    private static bool IsUsable(string? path)
        => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path);

    /// <summary>settings.json</summary>
    public static string SettingsFile => Path.Combine(DataRoot, "settings.json");

    /// <summary>tasks.json</summary>
    public static string TasksFile => Path.Combine(DataRoot, "tasks.json");

    /// <summary>notes.json — the notes widget's entries.</summary>
    public static string NotesFile => Path.Combine(DataRoot, "notes.json");

    /// <summary>agenda.json — the agenda widget's time-blocked items.</summary>
    public static string AgendaFile => Path.Combine(DataRoot, "agenda.json");

    /// <summary>Where the widget layout — which panel sits where — is persisted.</summary>
    public static string LayoutFile => Path.Combine(DataRoot, "layout.json");

    /// <summary>
    /// State of the Outpost desk (where each element sits, palette, 12/24h). Opaque JSON like
    /// <see cref="LayoutFile"/>; a separate file so the classic desk and the Outpost desk can
    /// never overwrite each other's arrangement.
    /// </summary>
    public static string OutpostFile => Path.Combine(DataRoot, "outpost.json");

    /// <summary>The sticky notes of the Outpost desk (text and colour). Positions stay in <see cref="OutpostFile"/>.</summary>
    public static string OutpostNotesFile => Path.Combine(DataRoot, "outpost-notes.json");

    /// <summary>logs</summary>
    public static string LogDirectory => Path.Combine(DataRoot, "logs");

    /// <summary>
    /// WebView2's own scratch space. Keep it out of the settings folder — and
    /// note this is the part that grows, which is why the root is redirectable.
    /// </summary>
    public static string WebViewUserData => Path.Combine(DataRoot, "webview2");

    /// <summary>Themes the user installed themselves.</summary>
    public static string UserThemes => Path.Combine(DataRoot, "themes");

    /// <summary>Widgets the user installed themselves.</summary>
    public static string UserWidgets => Path.Combine(DataRoot, "widgets");

    /// <summary>
    /// Dashboard themes the user installed themselves. A separate root from
    /// <see cref="UserThemes"/> — dashboard themes and desk themes are a
    /// different token contract (see docs/dashboard-theme-spec.md vs
    /// docs/theme-spec.md) and must not be scanned into the same picker.
    /// </summary>
    public static string UserDashboardThemes => Path.Combine(DataRoot, "dashboard-themes");

    /// <summary>The web layer that ships with the app (…\app\web).</summary>
    public static string BundledWeb => Path.Combine(AppContext.BaseDirectory, "web");

    /// <summary>Themes that ship with the app.</summary>
    public static string BundledThemes => Path.Combine(BundledWeb, "themes");

    /// <summary>Widgets that ship with the app.</summary>
    public static string BundledWidgets => Path.Combine(BundledWeb, "widgets");

    /// <summary>Dashboard themes that ship with the app.</summary>
    public static string BundledDashboardThemes => Path.Combine(BundledWeb, "dashboard", "themes");

    /// <summary>Creates every writable directory. Safe to call repeatedly.</summary>
    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(WebViewUserData);
        Directory.CreateDirectory(UserThemes);
        Directory.CreateDirectory(UserWidgets);
        Directory.CreateDirectory(UserDashboardThemes);
    }

    /// <summary>
    /// Where the web layer lives, for a WebView2 virtual host mapping.
    /// </summary>
    /// <remarks>
    /// An installed build has it beside the executable (<see cref="BundledWeb"/>).
    /// Running from source, that copy also exists (the build copies it there),
    /// but preferring the repo's own <c>web/</c> folder means editing a theme or
    /// a widget and reloading works without a rebuild. Shared by every native
    /// window that hosts the web layer — the desk and the dashboard both need
    /// exactly this same resolution, not two copies of it.
    /// </remarks>
    public static string ResolveWebRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && directory is not null; i++, directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "web", "shell", "index.html");
            if (File.Exists(candidate))
            {
                return Path.Combine(directory.FullName, "web");
            }
        }

        return BundledWeb;
    }
}
