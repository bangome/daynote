using System.Diagnostics;
using Daynote.App.Showcase;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ShowcaseInteractionCausalityTests
{
    public static IEnumerable<object[]> Families =>
        ShowcaseInteractionCatalog.Definitions.Select(definition => new object[] { definition.FamilyId });

    [TestMethod]
    [DynamicData(nameof(Families))]
    public async Task SequenceCli_WhenFixtureActionHandlersAreSuppressed_DoesNotClaimSemanticCompletion(
        string family)
    {
        var output = Path.Combine(Path.GetTempPath(), $"daynote-sequence-causality-{family}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(output);
        try
        {
            var executable = Path.Combine(
                Path.GetDirectoryName(typeof(ShowcaseManifest).Assembly.Location)!,
                "Daynote.App.exe");
            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            startInfo.Environment["DAYNOTE_SHOWCASE_DISABLE_INTERACTION_HANDLERS"] = "1";
            foreach (var argument in new[]
            {
                "--showcase", "--interaction-sequence", family,
                "--interaction-modality", "pointer", "--output", output,
                "--width", "1200", "--height", "600"
            })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo)!;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            await process.WaitForExitAsync(timeout.Token);
            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();

            Assert.AreNotEqual(
                0,
                process.ExitCode,
                $"The {family} fixture without its action handler must not claim semantic completion.{Environment.NewLine}" +
                $"stdout:{Environment.NewLine}{stdout}{Environment.NewLine}stderr:{Environment.NewLine}{stderr}");
            Assert.IsFalse(
                File.Exists(Path.Combine(output, "interaction-sequence.json")),
                $"Without the {family} fixture-owned action handler, no semantic receipt may be serialized.");
        }
        finally
        {
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }
}
