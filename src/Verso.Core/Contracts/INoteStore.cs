using Verso.Core.Models;

namespace Verso.Core.Contracts;

/// <summary>Reads and writes notes.json. Same durability contract as <see cref="ITaskStore"/>.</summary>
public interface INoteStore
{
    IReadOnlyList<NoteItem> Load();
    void Save(IReadOnlyList<NoteItem> notes);
}
