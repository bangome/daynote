using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Daynote.App.Showcase;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Daynote.App.Tests.ShowcaseSequenceTestHelpers;

namespace Daynote.App.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ShowcaseInteractionSequenceTests
{
    private static IReadOnlyDictionary<string, ShowcaseInteractionContract> Contracts =>
        ShowcaseInteractionContractTable.Contracts;

    public static IEnumerable<object[]> SequenceCases =>
        Contracts.Keys.SelectMany(family => new[]
        {
            new object[] { family, "pointer" },
            new object[] { family, "keyboard" }
        });

    [TestMethod]
    public void SemanticCatalog_MapsEveryAnimatedFamilyToItsExactActionAndStateContract()
    {
        Assert.HasCount(11, ShowcaseInteractionCatalog.Definitions);
        var actual = ShowcaseInteractionCatalog.Definitions.ToDictionary(
            definition => definition.FamilyId,
            StringComparer.Ordinal);
        CollectionAssert.AreEquivalent(Contracts.Keys.ToArray(), actual.Keys.ToArray());

        foreach (var (family, expected) in Contracts)
        {
            var definition = actual[family];
            Assert.AreEqual(expected.Page, definition.PageId, family);
            Assert.AreEqual(expected.Target, definition.InitiatorAutomationName, family);
            Assert.AreEqual(expected.ControlType, definition.InitiatorControlType, family);
            Assert.AreEqual(expected.Action, definition.SemanticAction, family);
            Assert.AreEqual("PreviewMouseLeftButtonUp", definition.PointerRoutedEvent, family);
            Assert.AreEqual(KeyboardTarget(family, expected), definition.KeyboardInitiatorAutomationName, family);
            Assert.AreEqual(expected.Key, definition.KeyboardKey, family);
            Assert.AreEqual(expected.MotionTarget, definition.MotionTargetAutomationName, family);
            Assert.AreEqual(expected.Before, definition.StateBefore, family);
            Assert.AreEqual(expected.After, definition.StateAfter, family);
            Assert.AreEqual(expected.FocusBefore, definition.FocusBefore, family);
            Assert.AreEqual(expected.Target, definition.FocusAfter, family);
            Assert.AreEqual(expected.ScrollOwner, definition.ScrollOwner, family);
            Assert.AreEqual(expected.ScrollOwnerAutomationName, definition.ScrollOwnerAutomationName, family);
        }

        var dateHeader = actual["date-header"];
        Assert.AreNotEqual("F6", dateHeader.KeyboardKey);
        Assert.IsFalse(dateHeader.InitiatorAutomationName.Contains("header", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    [DynamicData(nameof(SequenceCases))]
    public async Task SequenceCli_RecordsExactSemanticTransitionAndCorrelatedWpfFrames(
        string family,
        string modality)
    {
        var expected = Contracts[family];
        var output = Path.Combine(Path.GetTempPath(), $"daynote-sequence-{family}-{modality}-{Guid.NewGuid():N}");
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
            foreach (var argument in new[]
            {
                "--showcase", "--interaction-sequence", family,
                "--interaction-modality", modality, "--output", output,
                "--width", "1200", "--height", "600"
            })
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo)!;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                Assert.Fail($"The {family}/{modality} interaction sequence did not exit within 30 seconds.");
            }

            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            Assert.AreEqual(
                0,
                process.ExitCode,
                $"{family}/{modality}{Environment.NewLine}stdout:{Environment.NewLine}{stdout}{Environment.NewLine}stderr:{Environment.NewLine}{stderr}");
            var documentPath = Path.Combine(output, "interaction-sequence.json");
            Assert.IsTrue(File.Exists(documentPath), "Sequence mode must not fall through to static showcase capture.");

            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(documentPath));
            var root = document.RootElement;
            Assert.AreEqual("daynote.showcase-interaction-sequence/v1", String(root, "schema"));
            Assert.AreEqual(32, String(root, "runId").Length);
            Assert.AreEqual(family, String(root, "familyId"));
            Assert.AreEqual(expected.Page, String(root, "pageId"));
            Assert.AreEqual(modality, String(root, "modality"));
            Assert.AreEqual(expected.Action, String(root, "semanticAction"));
            var activeTarget = modality == "keyboard" ? KeyboardTarget(family, expected) : expected.Target;
            Assert.AreEqual(activeTarget, String(root, "initiatorAutomationName"));
            Assert.AreEqual(KeyboardTarget(family, expected), String(root, "keyboardInitiatorAutomationName"));
            Assert.AreEqual(expected.ControlType, String(root, "initiatorControlType"));
            Assert.AreEqual(expected.MotionTarget, String(root, "motionTargetAutomationName"));
            Assert.AreEqual(expected.ScrollOwner, String(root, "scrollOwner"));
            Assert.AreEqual(expected.ScrollOwnerAutomationName, String(root, "scrollOwnerAutomationName"));
            Assert.IsFalse(string.IsNullOrWhiteSpace(String(root, "pageRootAutomationName")));

            var executableSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(executable)));
            Assert.AreEqual(executableSha256, String(root, "executableSha256"));
            var processId = root.GetProperty("processId").GetInt32();
            var dispatcherThreadId = root.GetProperty("dispatcherThreadId").GetInt32();
            var hwnd = String(root, "windowHwnd");
            Assert.IsGreaterThan(0, processId);
            Assert.IsGreaterThan(0, dispatcherThreadId);
            Assert.AreNotEqual("0x0", hwnd);
            var ancestors = root.GetProperty("initiatorAncestorAutomationNames")
                .EnumerateArray().Select(item => item.GetString()).ToArray();
            CollectionAssert.Contains(ancestors, activeTarget);
            CollectionAssert.Contains(ancestors, String(root, "pageRootAutomationName"));

            var transitions = root.GetProperty("transitions").EnumerateArray().ToArray();
            Assert.HasCount(2, transitions);
            Assert.AreEqual("normal", String(transitions[0], "motion"));
            Assert.AreEqual("reduced", String(transitions[1], "motion"));
            Assert.AreNotEqual(String(transitions[0], "transitionId"), String(transitions[1], "transitionId"));

            foreach (var transition in transitions)
            {
                var transitionId = String(transition, "transitionId");
                Assert.AreEqual(32, transitionId.Length);
                Assert.AreEqual(processId, transition.GetProperty("processId").GetInt32());
                Assert.AreEqual(dispatcherThreadId, transition.GetProperty("dispatcherThreadId").GetInt32());
                Assert.AreEqual(hwnd, String(transition, "windowHwnd"));
                Assert.AreEqual(expected.Action, String(transition, "semanticAction"));
                Assert.AreEqual(activeTarget, String(transition, "initiatorAutomationName"));
                Assert.AreEqual(expected.ControlType, String(transition, "initiatorControlType"));
                Assert.AreEqual(expected.Before, String(transition, "stateBefore"));
                Assert.AreEqual(expected.After, String(transition, "stateAfterExpected"));
                Assert.AreEqual(expected.Before, String(transition, "semanticValueBeforeObserved"));
                Assert.AreEqual(expected.After, String(transition, "semanticValueAfterObserved"));
                Assert.AreEqual(expected.After, String(transition, "finalStateObserved"));
                Assert.AreNotEqual(
                    String(transition, "semanticValueBeforeObserved"),
                    String(transition, "semanticValueAfterObserved"));
                Assert.AreEqual(expected.Target, String(transition, "focusAfterObserved"));
                Assert.AreEqual(expected.ScrollOwner, String(transition, "scrollOwner"));
                Assert.AreEqual(expected.ScrollOwnerAutomationName, String(transition, "scrollOwnerObserved"));

                var receipt = transition.GetProperty("handlerReceipt");
                Assert.AreNotEqual(JsonValueKind.Null, receipt.ValueKind,
                    $"{family}/{modality} must record a fixture action-handler receipt.");
                Assert.AreEqual(expected.Action, String(receipt, "semanticAction"));
                Assert.IsFalse(string.IsNullOrWhiteSpace(String(receipt, "controlEvent")));
                Assert.IsFalse(string.IsNullOrWhiteSpace(String(receipt, "sourceAutomationName")));
                Assert.IsGreaterThan(0, receipt.GetProperty("sequence").GetInt64());

                var expectedFocusBefore = modality == "keyboard"
                    ? activeTarget
                    : expected.FocusBefore;
                Assert.AreEqual(expectedFocusBefore, String(transition, "focusBeforeObserved"));
                var events = transition.GetProperty("inputEvents").EnumerateArray().ToArray();
                Assert.IsGreaterThanOrEqualTo(6, events.Length);
                CollectionAssert.IsSubsetOf(
                    new[] { "input-manager-pre-process", "routed-preview", "input-manager-post-process" },
                    events.Select(item => String(item, "stage")).Distinct().ToArray());
                foreach (var inputEvent in events)
                {
                    Assert.AreEqual(transitionId, String(inputEvent, "transitionId"));
                    Assert.AreEqual(processId, inputEvent.GetProperty("processId").GetInt32());
                    Assert.AreEqual(dispatcherThreadId, inputEvent.GetProperty("dispatcherThreadId").GetInt32());
                    Assert.AreEqual(activeTarget, String(inputEvent, "originalSourceAutomationName"));
                    Assert.AreEqual(activeTarget, String(inputEvent, "targetAutomationName"));
                }

                if (modality == "pointer")
                {
                    Assert.IsTrue(events.Any(item =>
                        String(item, "stage") == "routed-preview" &&
                        String(item, "routedEvent") == "PreviewMouseUp" &&
                        String(item, "mouseButton") == "Left"));
                }
                else
                {
                    Assert.IsTrue(events.Any(item =>
                        String(item, "stage") == "routed-preview" &&
                        String(item, "routedEvent") == "PreviewKeyDown" &&
                        String(item, "key") == expected.Key));
                }
            }

            Assert.AreEqual(expected.DurationMilliseconds, transitions[0].GetProperty("durationMilliseconds").GetDouble(), 0.001);
            Assert.IsTrue(transitions[0].GetProperty("intermediateFrameObserved").GetBoolean());
            Assert.AreEqual(0d, transitions[1].GetProperty("durationMilliseconds").GetDouble(), 0.001);
            Assert.IsFalse(transitions[1].GetProperty("intermediateFrameObserved").GetBoolean());

            var normalFrames = transitions[0].GetProperty("frames").EnumerateArray().ToArray();
            var reducedFrames = transitions[1].GetProperty("frames").EnumerateArray().ToArray();
            CollectionAssert.AreEqual(
                new[] { "Rest0", "Midpoint100", "Settled" },
                normalFrames.Select(frame => String(frame, "semantic")).ToArray());
            CollectionAssert.AreEqual(
                new[] { "ReducedRest", "InstantSettled" },
                reducedFrames.Select(frame => String(frame, "semantic")).ToArray());
            ValidateFrames(output, transitions[0], normalFrames, processId, expected);
            ValidateFrames(output, transitions[1], reducedFrames, processId, expected);

            Assert.AreEqual(0d, normalFrames[0].GetProperty("opacity").GetDouble(), 0.001);
            Assert.IsGreaterThan(0d, normalFrames[1].GetProperty("opacity").GetDouble());
            Assert.IsLessThan(1d, normalFrames[1].GetProperty("opacity").GetDouble());
            Assert.AreEqual(1d, normalFrames[2].GetProperty("opacity").GetDouble(), 0.001);
            Assert.AreEqual(0d, reducedFrames[0].GetProperty("opacity").GetDouble(), 0.001);
            Assert.AreEqual(1d, reducedFrames[1].GetProperty("opacity").GetDouble(), 0.001);
            Assert.AreEqual(0d, reducedFrames[0].GetProperty("translateY").GetDouble(), 0.001);
            Assert.AreEqual(0d, reducedFrames[1].GetProperty("translateY").GetDouble(), 0.001);
            if (expected.Translates)
            {
                Assert.IsGreaterThan(0d, normalFrames[0].GetProperty("translateY").GetDouble());
                Assert.IsGreaterThan(0d, normalFrames[1].GetProperty("translateY").GetDouble());
                Assert.IsLessThan(
                    normalFrames[0].GetProperty("translateY").GetDouble(),
                    normalFrames[1].GetProperty("translateY").GetDouble());
            }
            else
            {
                Assert.IsTrue(normalFrames.All(frame => Math.Abs(frame.GetProperty("translateY").GetDouble()) < 0.001));
            }
            Assert.AreEqual(0d, normalFrames[2].GetProperty("translateY").GetDouble(), 0.001);
            Assert.AreEqual(0d, reducedFrames[1].GetProperty("translateY").GetDouble(), 0.001);
        }
        finally
        {
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

}
