using System.IO;
using System.Runtime.CompilerServices;
using Verso.Core;

namespace Verso.App.Tests;

/// <summary>
/// Points Verso at a throw-away data folder before anything reads <see cref="VersoPaths.DataRoot"/> (it is resolved
/// once). Without this the tests would write logs into the real %APPDATA%\Verso.
/// </summary>
internal static class TestEnvironment
{
    public static string DataRoot { get; } = Path.Combine(Path.GetTempPath(), "verso-tests-" + Guid.NewGuid().ToString("N"));

    [ModuleInitializer]
    internal static void Init() => Environment.SetEnvironmentVariable(VersoPaths.DataRootVariable, DataRoot);
}

/// <summary>A temp folder that is removed with the test.</summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "verso-t-" + Guid.NewGuid().ToString("N"));

    public TempDir() => Directory.CreateDirectory(Path);

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
