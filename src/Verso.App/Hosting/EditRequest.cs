namespace Verso.App.Hosting;

/// <summary>
/// A request from the desk to type into something. The desk page cannot take keyboard input (it lives behind the
/// desktop icons and only the mouse is mirrored into it), so the host opens a real window over the element.
/// </summary>
/// <param name="Kind">"note" (rich text) or "box" (a single line: a launcher box name).</param>
/// <param name="Id">The note or box the text belongs to.</param>
/// <param name="Value">The current text — HTML for a note, plain text for a box name.</param>
/// <param name="Color">The note's colour, for the editor's accent. May be empty.</param>
/// <param name="Single">One line, no toolbar; Enter commits.</param>
/// <param name="X">Screen position and size in physical pixels: exactly where the element is on screen.</param>
internal sealed record EditRequest(string Kind, string Id, string Value, string Color, bool Single, int X, int Y, int Width, int Height);
