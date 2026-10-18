using Verso.Core.Models;

namespace Verso.Core.Contracts;

/// <summary>Reads and writes agenda.json. Same durability contract as <see cref="ITaskStore"/>.</summary>
public interface IAgendaStore
{
    IReadOnlyList<AgendaItem> Load();
    void Save(IReadOnlyList<AgendaItem> items);
}
