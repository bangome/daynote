namespace Daynote.App.Tests;

internal static class TestPaths
{
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static string AppRoot => Path.Combine(RepositoryRoot, "src", "Daynote.App");

    public static string Theme(string fileName) => Path.Combine(AppRoot, "Themes", fileName);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DESIGN.md")) &&
                Directory.Exists(Path.Combine(directory.FullName, "src", "Daynote.App")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not find the Daynote repository above '{AppContext.BaseDirectory}'.");
    }
}
