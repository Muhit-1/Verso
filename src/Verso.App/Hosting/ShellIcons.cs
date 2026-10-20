using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Verso.App.Hosting;

/// <summary>
/// Icons for apps, as PNG data URLs the web layer can show.
/// </summary>
/// <remarks>
/// Asks the shell for the icon the Start menu itself would show (<c>IShellItemImageFactory</c>), at 64 px with
/// transparency. That covers programs, shortcuts, Store apps (<c>shell:AppsFolder\...</c>) and anything else the shell can
/// name. Falls back to the old small file icon for plain files. Results are cached per target.
/// </remarks>
internal static class ShellIcons
{
    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <returns>A <c>data:image/png</c> URL, or an empty string when the shell has no icon for it.</returns>
    public static string GetDataUrl(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return "";
        }

        return Cache.GetOrAdd(target, static t => FromShell(t) ?? FromFile(t) ?? "");
    }

    private static string? FromShell(string target)
    {
        IntPtr bitmap = IntPtr.Zero;
        try
        {
            var iid = typeof(IShellItemImageFactory).GUID;
            SHCreateItemFromParsingName(target, IntPtr.Zero, ref iid, out var factory);
            // 0x04 = icon only (never a document thumbnail)
            var hr = factory.GetImage(new Size64 { Cx = 64, Cy = 64 }, 0x04, out bitmap);
            if (hr != 0 || bitmap == IntPtr.Zero)
            {
                return null;
            }

            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or IOException or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
        finally
        {
            if (bitmap != IntPtr.Zero)
            {
                DeleteObject(bitmap);
            }
        }
    }

    private static string? FromFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var icon = Icon.ExtractAssociatedIcon(path);
            if (icon is null)
            {
                return null;
            }

            using var bitmap = icon.ToBitmap();
            using var stream = new MemoryStream();
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            return "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Size64
    {
        public int Cx;
        public int Cy;
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(Size64 size, uint flags, out IntPtr bitmap);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        string path, IntPtr bindContext, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr handle);
}
