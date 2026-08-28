using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace Verso.Interop;

/// <summary>
/// Sets the real Windows desktop wallpaper.
/// </summary>
/// <remarks>
/// Windows has no video-wallpaper concept of its own, so this only ever
/// handles the "image" background kind — see <c>DeskWindow</c> for how
/// "video" and "interactive" are drawn instead, inside the web layer.
/// </remarks>
public static class DesktopWallpaper
{
    /// <param name="path">Absolute path to an image file.</param>
    public static unsafe void SetImage(string path)
    {
        fixed (char* pvParam = path)
        {
            PInvoke.SystemParametersInfo(
                SYSTEM_PARAMETERS_INFO_ACTION.SPI_SETDESKWALLPAPER,
                0,
                pvParam,
                SYSTEM_PARAMETERS_INFO_UPDATE_FLAGS.SPIF_UPDATEINIFILE
                    | SYSTEM_PARAMETERS_INFO_UPDATE_FLAGS.SPIF_SENDCHANGE);
        }
    }
}
