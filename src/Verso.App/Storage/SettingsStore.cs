using Verso.Core;
using Verso.Core.Models;

namespace Verso.App.Storage;

/// <summary>settings.json in %APPDATA%\Verso, cached in memory.</summary>
internal sealed class SettingsStore
{
    private readonly Lock _gate = new();
    private VersoSettings _current;

    public SettingsStore()
    {
        _current = JsonFile.Read(VersoPaths.SettingsFile, new VersoSettings());
    }

    public VersoSettings Current
    {
        get { lock (_gate) { return _current; } }
    }

    /// <summary>Mutates and persists in one step, so the two can never drift.</summary>
    public void Update(Action<VersoSettings> mutate)
    {
        lock (_gate)
        {
            mutate(_current);
            JsonFile.Write(VersoPaths.SettingsFile, _current);
        }
    }
}
