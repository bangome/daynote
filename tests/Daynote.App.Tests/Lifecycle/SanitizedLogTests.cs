using System.Text.RegularExpressions;
using Daynote.App.Lifecycle;
using Daynote.Core.Diagnostics;
using Daynote.Core.Notes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Lifecycle;

[TestClass]
public sealed class SanitizedLogTests
{
    private static readonly Regex PayloadFreeLine =
        new(@"^lifecycle event=[A-Za-z]+( code=-?\d+)?$", RegexOptions.Compiled);

    [TestMethod]
    public void Test_SanitizedLog_api_cannot_accept_free_text_payload()
    {
        // Payload-freeness is structural: no method on the contract accepts a string.
        foreach (var method in typeof(ISanitizedLog).GetMethods())
        {
            foreach (var parameter in method.GetParameters())
            {
                Assert.AreNotEqual(typeof(string), parameter.ParameterType,
                    $"{method.Name} must not accept string payload.");
            }
        }
    }

    [TestMethod]
    public void Test_TextWriterSanitizedLog_emits_only_event_names_and_numeric_codes()
    {
        var writer = new StringWriter();
        var log = new TextWriterSanitizedLog(writer);

        foreach (LifecycleEvent lifecycleEvent in Enum.GetValues<LifecycleEvent>())
        {
            log.Record(lifecycleEvent);
            log.Record(lifecycleEvent, 7);
        }

        foreach (string line in writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            Assert.IsTrue(PayloadFreeLine.IsMatch(line), $"Non-payload-free log line: '{line}'.");
        }
    }

    [TestMethod]
    public async Task Test_Coordinator_lifecycle_logging_never_contains_sentinel_payload()
    {
        // A sentinel that stands in for note content. It is never handed to the coordinator; this proves
        // that even a full lifecycle run emits no free text that could carry payload.
        const string sentinel = "SENTINEL-비밀-note-body-42";
        var writer = new StringWriter();
        var tray = new RecordingTrayPresenter();
        var window = new RecordingWindowHost();
        var exit = new RecordingApplicationExit();

        var coordinator = new AppLifecycleCoordinator(
            tray, window, exit,
            (_, _) => Task.FromResult(FlushResult.Proceed),
            new TextWriterSanitizedLog(writer));

        coordinator.HideToTray();
        coordinator.ShowWindow();
        await coordinator.QuitAsync();

        string output = writer.ToString();
        Assert.IsFalse(output.Contains(sentinel, StringComparison.Ordinal), "The log must never contain payload.");
        StringAssert.Contains(output, nameof(LifecycleEvent.QuitCompleted));
        foreach (string line in output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            Assert.IsTrue(PayloadFreeLine.IsMatch(line), $"Non-payload-free log line: '{line}'.");
        }
    }
}
