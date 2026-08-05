using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests;

internal static class ShowcaseSequenceTestHelpers
{
    internal static void ValidateFrames(
        string output,
        JsonElement transition,
        IReadOnlyList<JsonElement> frames,
        int processId,
        ShowcaseInteractionContract expected)
    {
        var transitionId = String(transition, "transitionId");
        for (var index = 0; index < frames.Count; index++)
        {
            var frame = frames[index];
            Assert.AreEqual(transitionId, String(frame, "transitionId"));
            Assert.AreEqual(processId, frame.GetProperty("processId").GetInt32());
            Assert.AreEqual(1200, frame.GetProperty("pixelWidth").GetInt32());
            Assert.AreEqual(600, frame.GetProperty("pixelHeight").GetInt32());
            Assert.AreEqual(index == 0 ? expected.Before : expected.After, String(frame, "stateObserved"));
            Assert.AreEqual(expected.ScrollOwnerAutomationName, String(frame, "scrollOwnerObserved"));
            var pngPath = Path.Combine(output, String(frame, "png"));
            Assert.IsTrue(File.Exists(pngPath), pngPath);
            var bytes = File.ReadAllBytes(pngPath);
            CollectionAssert.AreEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes.Take(8).ToArray());
            Assert.AreEqual(Convert.ToHexString(SHA256.HashData(bytes)), String(frame, "pngSha256"));
        }
    }

    internal static string String(JsonElement element, string property) =>
        element.GetProperty(property).ValueKind == JsonValueKind.Null
            ? string.Empty
            : element.GetProperty(property).GetString()!;

    internal static string KeyboardTarget(string family, ShowcaseInteractionContract expected) =>
        family == "note-tab" ? "Note 1" : expected.Target;
}
