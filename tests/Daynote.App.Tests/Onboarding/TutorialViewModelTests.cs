using System.Windows.Input;
using Daynote.App.Input;
using Daynote.App.Onboarding;
using Daynote.App.Tests.Lifecycle;
using Daynote.Core.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Onboarding;

[TestClass]
public sealed class TutorialViewModelTests
{
    private static TutorialViewModel Create(InMemorySettingsStore? store = null, FakeStartupTaskService? startup = null)
    {
        store ??= new InMemorySettingsStore();
        return new TutorialViewModel(
            store, new ConfigurableShortcuts(store),
            startup ?? new FakeStartupTaskService(Daynote.Core.Startup.StartupTaskState.Unavailable));
    }

    [TestMethod]
    public async Task Startup_toggle_enables_when_the_task_is_togglable()
    {
        var startup = new FakeStartupTaskService(Daynote.Core.Startup.StartupTaskState.Disabled);
        TutorialViewModel vm = Create(startup: startup);
        await vm.LoadAsync();

        Assert.IsTrue(vm.StartupToggleEnabled);
        Assert.IsFalse(vm.StartupIsOn);

        await vm.ToggleStartupCommand.ExecuteAsync(null);

        Assert.IsTrue(vm.StartupIsOn);
        Assert.AreEqual(1, startup.EnableCalls);
    }

    [TestMethod]
    public async Task Startup_toggle_is_disabled_when_unavailable()
    {
        var startup = new FakeStartupTaskService(Daynote.Core.Startup.StartupTaskState.Unavailable);
        TutorialViewModel vm = Create(startup: startup);
        await vm.LoadAsync();

        Assert.IsFalse(vm.StartupToggleEnabled);
        await vm.ToggleStartupCommand.ExecuteAsync(null);
        Assert.AreEqual(0, startup.EnableCalls, "An unavailable startup task is never enabled from the tutorial.");
    }

    [TestMethod]
    public void Steps_map_to_the_expected_spotlight_targets()
    {
        TutorialViewModel vm = Create();

        // Welcome + shortcuts steps are centered (no target); the rest point at a named element.
        Assert.IsNull(vm.Steps[0].TargetName);
        Assert.AreEqual(TutorialTargets.Calendar, vm.Steps[1].TargetName);
        Assert.AreEqual(TutorialTargets.TabTodo, vm.Steps[2].TargetName);
        Assert.AreEqual(TutorialTargets.TabFiles, vm.Steps[3].TargetName);
        Assert.AreEqual(TutorialTargets.Editor, vm.Steps[4].TargetName);
        Assert.AreEqual(TutorialTargets.Search, vm.Steps[5].TargetName);
        Assert.IsTrue(vm.Steps[6].IsShortcuts);
        Assert.IsNull(vm.Steps[6].TargetName);
        Assert.AreEqual(TutorialTargets.Settings, vm.Steps[7].TargetName);
    }

    [TestMethod]
    public void Next_and_Back_stay_within_bounds_and_update_progress()
    {
        TutorialViewModel vm = Create();
        vm.Open();

        Assert.IsTrue(vm.IsFirst);
        Assert.AreEqual("1 / " + vm.Steps.Count, vm.ProgressText);
        vm.BackCommand.Execute(null); // no-op at first
        Assert.IsTrue(vm.IsFirst);

        for (int i = 0; i < vm.Steps.Count + 3; i++)
        {
            vm.NextCommand.Execute(null);
        }

        Assert.IsTrue(vm.IsLast);
        Assert.AreEqual($"{vm.Steps.Count} / {vm.Steps.Count}", vm.ProgressText);

        vm.BackCommand.Execute(null);
        Assert.IsFalse(vm.IsLast);
    }

    [TestMethod]
    public async Task Finish_persists_completed_closes_and_resolves()
    {
        var store = new InMemorySettingsStore();
        TutorialViewModel vm = Create(store);
        bool resolved = false;
        vm.Resolved += (_, _) => resolved = true;
        vm.Open();

        vm.FinishCommand.Execute(null);
        await Task.Yield();

        Assert.IsFalse(vm.IsOpen);
        Assert.IsTrue(resolved);
        Assert.IsTrue(await store.GetBoolAsync(OnboardingSettings.CompletedKey, false));
    }

    [TestMethod]
    public async Task Skip_marks_completed()
    {
        var store = new InMemorySettingsStore();
        TutorialViewModel vm = Create(store);
        vm.Open();

        vm.SkipCommand.Execute(null);
        await Task.Yield();

        Assert.IsTrue(await store.GetBoolAsync(OnboardingSettings.CompletedKey, false));
    }

    [TestMethod]
    public async Task ShouldAutoShow_reflects_the_completed_flag()
    {
        var store = new InMemorySettingsStore();
        TutorialViewModel first = Create(store);
        await first.LoadAsync();
        Assert.IsTrue(first.ShouldAutoShow, "A fresh install auto-shows the tutorial.");

        await store.SetBoolAsync(OnboardingSettings.CompletedKey, true);
        TutorialViewModel second = Create(store);
        await second.LoadAsync();
        Assert.IsFalse(second.ShouldAutoShow, "Once completed it never auto-shows again.");

        // Re-open from Settings works regardless of the completed flag.
        second.Open();
        Assert.IsTrue(second.IsOpen);
        Assert.IsTrue(second.IsFirst);
    }

    [TestMethod]
    public async Task Shortcuts_list_includes_globals_and_current_in_app_gestures()
    {
        var store = new InMemorySettingsStore();
        var shortcuts = new ConfigurableShortcuts(store);
        await shortcuts.SetAsync(AppShortcuts.NewNote, new Hotkey(ModifierKeys.Control | ModifierKeys.Shift, Key.M));
        var vm = new TutorialViewModel(store, shortcuts, new FakeStartupTaskService(Daynote.Core.Startup.StartupTaskState.Unavailable));
        await vm.LoadAsync();

        var gestures = vm.Shortcuts.Select(h => h.Gesture).ToList();
        Assert.IsTrue(gestures.Contains("Ctrl+Alt+D"), "Global summon default is listed.");
        Assert.IsTrue(gestures.Contains("Alt+`"), "Quick-sticky global is listed.");
        Assert.IsTrue(gestures.Contains("Ctrl+Shift+M"), "The reassigned in-app gesture is reflected.");
    }
}
