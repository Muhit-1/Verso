namespace Verso.Interop;

/// <summary>
/// A native window a desk's WebView2 can be hosted in.
/// </summary>
/// <remarks>
/// Two implementations: <see cref="DesktopSurface"/>, a top-level bottom-most
/// window reshaped to the widget panels, and <see cref="ProgmanSurface"/>, a
/// layered child of Progman sitting behind the desktop icons. The desk does not
/// care which — it only needs a handle to host in, a size, and a way to put the
/// window back where it belongs.
/// </remarks>
public interface IDeskSurface : IDisposable
{
    /// <summary>The window handle. Pass this to WebView2's Win32 hosting API.</summary>
    nint Handle { get; }

    /// <summary>The client area, in physical pixels.</summary>
    (int Width, int Height) ClientSize { get; }

    /// <summary>Puts the window back where it belongs in the Z-order.</summary>
    void PinToBottom();

    /// <summary>Shows or hides the window, without ever activating it.</summary>
    void SetVisible(bool visible);
}
