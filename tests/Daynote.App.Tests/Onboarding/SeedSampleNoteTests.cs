using Daynote.App.Tests.Lifecycle;
using Daynote.App.Tests.Workspace;
using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;
using Daynote.Core.Notes;
using Daynote.Core.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Onboarding;

[TestClass]
public sealed class SeedSampleNoteTests
{
    private static NoteId NextId() => NoteId.Create(Guid.NewGuid()).Value;

    [TestMethod]
    public async Task Seeds_a_note_once_on_an_empty_date_and_marks_the_flag()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        var settings = new InMemorySettingsStore();
        var seed = new SeedSampleNote(context.NoteRepository, settings, NextId);
        LocalDate today = WorkspaceTestContext.Date("2026-07-20");

        bool created = await seed.ExecuteAsync(today, "예시", "본문 -[] 할 일 (7/20 10:00)");

        Assert.IsTrue(created);
        Assert.IsTrue(await settings.GetBoolAsync(OnboardingSettings.SampleSeededKey, false));
        DayWorkspace ws = await context.NoteRepository.GetDayWorkspaceStateAsync(today);
        Assert.IsFalse(ws.Notes.IsProjectionOnly, "The sample note is now the date's real note.");

        // Second run is a no-op (flag already set) — no duplicate.
        bool again = await seed.ExecuteAsync(today, "예시", "본문");
        Assert.IsFalse(again);
    }

    [TestMethod]
    public async Task Relocalize_rewrites_the_untouched_sample_into_the_new_language()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        var settings = new InMemorySettingsStore();
        var seed = new SeedSampleNote(context.NoteRepository, settings, NextId);
        LocalDate today = WorkspaceTestContext.Date("2026-07-20");
        await seed.ExecuteAsync(today, "예시", "한국어 본문");

        LocalDate? changed = await seed.RelocalizeAsync("Sample", _ => "English body");

        Assert.AreEqual(today, changed);
        DayWorkspace ws = await context.NoteRepository.GetDayWorkspaceStateAsync(today);
        Note note = ws.Notes.Notes.Single(n => !n.IsProjection);
        Assert.AreEqual("English body", note.Body);
        Assert.AreEqual("Sample", note.Title);
    }

    [TestMethod]
    public async Task Relocalize_leaves_an_edited_sample_alone()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        var settings = new InMemorySettingsStore();
        var seed = new SeedSampleNote(context.NoteRepository, settings, NextId);
        LocalDate today = WorkspaceTestContext.Date("2026-07-20");
        await seed.ExecuteAsync(today, "예시", "한국어 본문");

        // User edits the sample: find it and save a different body.
        DayWorkspace before = await context.NoteRepository.GetDayWorkspaceStateAsync(today);
        Note edited = before.Notes.Notes.Single(n => !n.IsProjection);
        await context.NoteRepository.SaveNoteAsync(new NoteSaveRequest(
            edited.Id!.Value, today, edited.Title, "사용자가 고침", before.RevisionOf(edited.Id!.Value), IsNew: false, HasCustomTitle: true));

        LocalDate? changed = await seed.RelocalizeAsync("Sample", _ => "English body");

        Assert.IsNull(changed, "An edited sample is never overwritten.");
        DayWorkspace after = await context.NoteRepository.GetDayWorkspaceStateAsync(today);
        Assert.AreEqual("사용자가 고침", after.Notes.Notes.Single(n => !n.IsProjection).Body);
    }

    [TestMethod]
    public async Task Does_not_touch_a_date_that_already_has_notes()
    {
        await using WorkspaceTestContext context = WorkspaceTestContext.Create();
        LocalDate today = WorkspaceTestContext.Date("2026-07-20");
        await context.StoreNoteAsync(today, "기존 노트", "사용자 본문");
        var settings = new InMemorySettingsStore();
        var seed = new SeedSampleNote(context.NoteRepository, settings, NextId);

        bool created = await seed.ExecuteAsync(today, "예시", "예시 본문");

        Assert.IsFalse(created, "Existing data is never disturbed.");
        Assert.IsTrue(await settings.GetBoolAsync(OnboardingSettings.SampleSeededKey, false), "Still marked so it won't retry later.");
    }
}
