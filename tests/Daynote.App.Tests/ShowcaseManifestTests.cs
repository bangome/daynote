using System.Text.Json;
using Daynote.App.Showcase;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests;

[TestClass]
public sealed class ShowcaseManifestTests
{
    private static readonly IReadOnlyDictionary<string, string[]> ExpectedStates =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["app-shell"] = ["default", "focus", "disabled", "loading", "empty", "error"],
            ["workspace-view-switch"] = ["default", "hover", "active", "focus", "disabled", "loading", "error"],
            ["pane-splitter"] = ["default", "hover", "active", "focus", "disabled"],
            ["calendar-day"] = ["default", "hover", "active", "focus", "disabled", "loading", "error"],
            ["date-header"] = ["default", "loading", "empty", "error"],
            ["note-tab"] = ["default", "hover", "active", "focus", "disabled", "loading", "empty", "error"],
            ["markdown-editor"] = ["default", "active", "focus", "disabled", "loading", "empty", "error"],
            ["editor-toolbar"] = ["default", "hover", "active", "focus", "disabled", "loading", "error"],
            ["clipboard-item"] = ["default", "hover", "active", "focus", "disabled", "loading", "empty", "error"],
            ["sidebar-note-list"] = ["default", "hover", "active", "focus", "disabled", "loading", "empty", "error"],
            ["clipboard-drawer"] = ["default", "hover", "active", "focus", "disabled", "loading", "empty", "error"],
            ["search"] = ["default", "hover", "active", "focus", "disabled", "loading", "empty", "error"],
            ["button"] = ["default", "hover", "active", "focus", "disabled", "loading", "error"],
            ["status-banner"] = ["default", "hover", "active", "focus", "disabled", "loading", "empty", "error"],
            ["consent-panel"] = ["default", "hover", "active", "focus", "disabled", "loading", "error"],
            ["settings-row"] = ["default", "hover", "active", "focus", "disabled", "loading", "empty", "error"],
            ["tray-menu"] = ["default", "hover", "active", "focus", "disabled", "loading", "error"],
            ["patterns"] = ["hover", "active", "focus", "disabled", "loading", "empty", "error"]
        };

    [TestMethod]
    public void Primitives_MatchTheSection5ApplicabilityMatrix()
    {
        var actual = ShowcaseManifest.Primitives.ToDictionary(
            primitive => primitive.Id,
            primitive => primitive.States.ToArray(),
            StringComparer.Ordinal);

        CollectionAssert.AreEquivalent(ExpectedStates.Keys.ToArray(), actual.Keys.ToArray());
        foreach (var expected in ExpectedStates)
            CollectionAssert.AreEqual(expected.Value, actual[expected.Key], $"State mismatch for {expected.Key}.");
    }

    [TestMethod]
    public void Pages_ExpandEveryApplicableStateAcrossCompactRegularAndWide()
    {
        var expectedIds = Enum.GetValues<ShowcaseLayout>()
            .SelectMany(layout => ExpectedStates.SelectMany(primitive => primitive.Value.Select(state =>
                $"{layout.ToString().ToLowerInvariant()}.{primitive.Key}.{state}")))
            .ToArray();
        var actualIds = ShowcaseManifest.Pages.Select(page => page.Id).ToArray();

        Assert.AreEqual(expectedIds.Length, actualIds.Distinct(StringComparer.Ordinal).Count());
        CollectionAssert.AreEquivalent(expectedIds, actualIds);
    }

    [TestMethod]
    public void Pages_CarryActionableAutomationFocusAndScrollMetadata()
    {
        foreach (var page in ShowcaseManifest.Pages)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(page.AutomationName), page.Id);
            Assert.IsFalse(string.IsNullOrWhiteSpace(page.FocusOwner), page.Id);
            Assert.IsFalse(string.IsNullOrWhiteSpace(page.ScrollOwner), page.Id);
            StringAssert.Contains(page.AutomationName, page.State, page.Id);
            StringAssert.Contains(page.AutomationName, page.Layout.ToString(), page.Id);
        }
    }

    [TestMethod]
    public void FindPage_DefaultsToReferenceCaptureAndMatchesCaseInsensitively()
    {
        Assert.AreEqual("wide.app-shell.default", ShowcaseManifest.FindPage(null).Id);
        Assert.AreEqual("regular.search.focus", ShowcaseManifest.FindPage("REGULAR.SEARCH.FOCUS").Id);
    }

    [TestMethod]
    public void FindPage_UnknownId_ExplainsHowToDiscoverValidIds()
    {
        var exception = Assert.ThrowsExactly<ArgumentException>(
            () => ShowcaseManifest.FindPage("wide.unknown.default"));

        StringAssert.Contains(exception.Message, "--list");
    }

    [TestMethod]
    public void CreateDocument_RecordsSchemaBuildIdentityAndLatestSourceTime()
    {
        var root = Path.Combine(Path.GetTempPath(), $"daynote-manifest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var older = Path.Combine(root, "older.cs");
            var latest = Path.Combine(root, "latest.xaml");
            var ignored = Path.Combine(root, "ignored.txt");
            File.WriteAllText(older, "source");
            File.WriteAllText(latest, "resource");
            File.WriteAllText(ignored, "not source");
            File.SetLastWriteTimeUtc(older, new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(latest, new DateTime(2025, 2, 2, 3, 4, 6, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(ignored, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            var document = ShowcaseManifest.CreateDocument(root);

            Assert.AreEqual("daynote.showcase/v1", document.Schema);
            StringAssert.Contains(document.BuildIdentity, "+mvid.");
            Assert.AreEqual(new DateTimeOffset(2025, 2, 2, 3, 4, 6, TimeSpan.Zero), document.SourceModifiedUtc);
            Assert.AreEqual(ExpectedStates.Count, document.PrimitiveCount);
            Assert.AreEqual(ShowcaseManifest.Pages.Count, document.PageCount);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public void JsonOptions_EmitCamelCaseAndKebabCaseEnumValues()
    {
        var page = ShowcaseManifest.FindPage("wide.app-shell.default");
        var json = JsonSerializer.Serialize(page, ShowcaseManifest.JsonOptions);

        StringAssert.Contains(json, "\"primitiveId\"");
        StringAssert.Contains(json, "\"layout\": \"wide\"");
        Assert.IsFalse(json.Contains("PrimitiveId", StringComparison.Ordinal));
    }
}
