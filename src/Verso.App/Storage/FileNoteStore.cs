using Verso.Core;
using Verso.Core.Contracts;
using Verso.Core.Models;

namespace Verso.App.Storage;

/// <summary>notes.json in %APPDATA%\Verso. Mirrors <see cref="FileTaskStore"/> exactly.</summary>
internal sealed class FileNoteStore : INoteStore
{
    private readonly Lock _gate = new();

    public IReadOnlyList<NoteItem> Load()
    {
        lock (_gate)
        {
            return JsonFile.Read(VersoPaths.NotesFile, new List<NoteItem>());
        }
    }

    public void Save(IReadOnlyList<NoteItem> notes)
    {
        lock (_gate)
        {
            JsonFile.Write(VersoPaths.NotesFile, notes);
        }
    }
}
