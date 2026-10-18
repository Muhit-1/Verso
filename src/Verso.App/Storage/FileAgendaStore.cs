using Verso.Core;
using Verso.Core.Contracts;
using Verso.Core.Models;

namespace Verso.App.Storage;

/// <summary>agenda.json in %APPDATA%\Verso. Mirrors <see cref="FileTaskStore"/> exactly.</summary>
internal sealed class FileAgendaStore : IAgendaStore
{
    private readonly Lock _gate = new();

    public IReadOnlyList<AgendaItem> Load()
    {
        lock (_gate)
        {
            return JsonFile.Read(VersoPaths.AgendaFile, new List<AgendaItem>());
        }
    }

    public void Save(IReadOnlyList<AgendaItem> items)
    {
        lock (_gate)
        {
            JsonFile.Write(VersoPaths.AgendaFile, items);
        }
    }
}
