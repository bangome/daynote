using Avalonia;
using Daynote.App.Composition;
using Daynote.Desktop.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace Daynote.Desktop.Tests;

/// <summary>The app's real service graph, pointed at a throwaway data root.</summary>
internal static class TestServices
{
    internal static ServiceProvider Build(string dataRoot, Application application)
    {
        Environment.SetEnvironmentVariable("DAYNOTE_DATA_ROOT", dataRoot);
        var services = new ServiceCollection();
        services.AddDaynoteDesktop(
            DaynoteAppOptions.ForCurrentUser(),
            application,
            () => null,
            () => { });
        return services.BuildServiceProvider();
    }
}

/// <summary>A throwaway data root, so a test never opens the developer's own database.</summary>
internal sealed class TempDataRoot : IDisposable
{
    internal string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "daynote-tests", Guid.NewGuid().ToString("N"));

    internal TempDataRoot() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A SQLite handle can outlive the test by a moment; a leftover temp folder is not a
            // failure worth turning a green run red.
        }
    }
}
