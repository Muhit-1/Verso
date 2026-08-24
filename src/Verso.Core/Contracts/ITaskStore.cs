using Verso.Core.Models;

namespace Verso.Core.Contracts;

/// <summary>Reads and writes tasks.json.</summary>
/// <remarks>
/// Writes go to a temp file and are then moved over the original, so a crash
/// mid-write loses the change rather than the file.
/// </remarks>
public interface ITaskStore
{
    IReadOnlyList<TaskItem> Load();
    void Save(IReadOnlyList<TaskItem> tasks);
}
