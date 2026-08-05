using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests;

[TestClass]
public sealed class SourceStructureTests
{
    [TestMethod]
    public void HandwrittenAppFiles_RemainReviewableInsteadOfBecomingMonoliths()
    {
        var limits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [".cs"] = 500,
            [".xaml"] = 700
        };
        var violations = Directory.EnumerateFiles(TestPaths.AppRoot, "*", SearchOption.AllDirectories)
            .Where(path => limits.ContainsKey(Path.GetExtension(path)))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(path => new
            {
                Path = path,
                Lines = File.ReadLines(path).Count(),
                Limit = limits[Path.GetExtension(path)]
            })
            .Where(file => file.Lines > file.Limit)
            .Select(file => $"{Path.GetRelativePath(TestPaths.RepositoryRoot, file.Path)} ({file.Lines}>{file.Limit})")
            .ToArray();

        Assert.AreEqual(0, violations.Length, $"Split oversized source files: {string.Join(", ", violations)}");
    }
}
