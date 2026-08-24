using Verso.Core;
using Verso.Core.Contracts;
using Verso.Core.Models;

namespace Verso.App.Storage;

/// <summary>tasks.json in %APPDATA%\Verso. Deliberately not a database.</summary>
internal sealed class FileTaskStore : ITaskStore
{
    private readonly Lock _gate = new();

    public IReadOnlyList<TaskItem> Load()
    {
        lock (_gate)
        {
            return JsonFile.Read(VersoPaths.TasksFile, new List<TaskItem>());
        }
    }

    public void Save(IReadOnlyList<TaskItem> tasks)
    {
        lock (_gate)
        {
            JsonFile.Write(VersoPaths.TasksFile, tasks);
        }
    }
}
