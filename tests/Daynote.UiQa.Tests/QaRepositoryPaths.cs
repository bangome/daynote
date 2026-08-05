namespace Daynote.UiQa.Tests;

internal static class QaRepositoryPaths
{
    public static string RepositoryRoot { get; } = FindRepositoryRoot();

    public static string QaDirectory => Path.Combine(RepositoryRoot, "qa");

    public static IReadOnlyList<string> QaScripts() =>
        Directory.GetFiles(QaDirectory, "*.ps1", SearchOption.TopDirectoryOnly);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DESIGN.md")) &&
                Directory.Exists(Path.Combine(directory.FullName, "qa")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not find the Daynote repository above '{AppContext.BaseDirectory}'.");
    }
}
