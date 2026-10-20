using System.IO;

namespace Verso.App.Hosting;

/// <summary>
/// The icons currently selected on the real Windows desktop (Explorer's icon layer, in front of the wallpaper page).
/// </summary>
/// <remarks>
/// Dragging a desktop icon onto a launcher box never reaches Verso as a drag-and-drop — Explorer's icons are in front and
/// receive the drop themselves (and move the icon). What Verso does see is the mouse (mirrored into the page), and what
/// Explorer has selected: pressing an icon selects it at once, and the selection stays readable during and after the drag.
/// Spike result in CLAUDE.md ("S0 part B").
/// </remarks>
internal static class DesktopSelection
{
    public sealed record Item(string Name, string Path);

    private const int TimeoutMs = 1500;

    /// <summary>
    /// The selected items that have a real file or folder behind them. "This PC", "Recycle Bin" and the like have no path
    /// (<c>::{...}</c>) and are left out. Never throws; an unreadable desktop is an empty list.
    /// </summary>
    public static List<Item> Read()
    {
        var result = new List<Item>();
        var thread = new Thread(() =>
        {
            try
            {
                result = ReadOnSta();
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
            {
                result = [];
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        return thread.Join(TimeoutMs) ? result : [];
    }

    private static List<Item> ReadOnSta()
    {
        var items = new List<Item>();

        // ShellWindows -> the desktop's view -> SelectedItems (the same route Explorer's own scripting uses)
        var shellWindows = Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"));
        if (shellWindows is null)
        {
            return items;
        }

        dynamic windows = Activator.CreateInstance(shellWindows)!;
        object csidlDesktop = 0;
        object location = null!;
        int hwnd = 0;
        dynamic? view = windows.FindWindowSW(ref csidlDesktop, ref location, 8 /* SWC_DESKTOP */, ref hwnd, 1 /* SWFO_NEEDDISPATCH */);
        if (view is null)
        {
            return items;
        }

        foreach (dynamic selected in view.Document.SelectedItems())
        {
            string name = selected.Name ?? "";
            string path = selected.Path ?? "";
            if (path.Length == 0 || path.StartsWith("::", StringComparison.Ordinal))
            {
                continue;
            }

            if (File.Exists(path) || Directory.Exists(path))
            {
                items.Add(new Item(name, path));
            }
        }

        return items;
    }
}
