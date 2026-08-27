using System.Windows.Input;
using Daynote.App.Input;
using Daynote.App.Settings;
using Daynote.Core.Settings;
using Daynote.Core.Startup;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.App.Tests.Lifecycle;

[TestClass]
public sealed class SettingsViewModelTests
{
    private static async Task<(SettingsViewModel Vm, SettingsViewModelTestHooks Hooks)> CreateAsync(
        StartupTaskState startupState, bool flushSucceeds = true)
    {
        var settingsStore = new InMemorySettingsStore();
        var startup = new FakeStartupTaskService(startupState);
        var hotkeys = new RecordingHotkeyService();
        var backup = new FakeBackupService();
        var picker = new FakeBackupFilePicker();
        var shortcuts = new ConfigurableShortcuts(settingsStore);
        var hooks = new SettingsViewModelTestHooks(startup, hotkeys, settingsStore, backup, picker, shortcuts);
        var vm = new SettingsViewModel(
            startup, hotkeys, settingsStore, backup, picker, shortcuts,
            () => Task.FromResult(flushSucceeds),
            () => hooks.RestartRequests++,
            () => hooks.TutorialRequests++,
            @"C:\Users\Test\AppData\Local\Daynote");
        await vm.LoadAsync();
        return (vm, hooks);
    }

    private sealed record SettingsViewModelTestHooks(
        FakeStartupTaskService Startup,
        RecordingHotkeyService Hotkeys,
        InMemorySettingsStore Settings,
        FakeBackupService Backup,
        FakeBackupFilePicker Picker,
        ConfigurableShortcuts Shortcuts)
    {
        public int RestartRequests { get; set; }

        public int TutorialRequests { get; set; }
    }

    [TestMethod]
    public async Task Test_Startup_toggle_is_enabled_for_a_plain_disabled_state()
    {
        (SettingsViewModel vm, _) = await CreateAsync(StartupTaskState.Disabled);

        Assert.IsTrue(vm.StartupToggleEnabled);
        Assert.IsFalse(vm.StartupIsOn);
        StringAssert.Contains(vm.StartupStateText, "자동으로 실행되지 않습니다");
    }

    [TestMethod]
    public async Task Test_Startup_toggle_disabled_by_user_shows_policy_text_and_stays_disabled()
    {
        (SettingsViewModel vm, SettingsViewModelTestHooks hooks) = await CreateAsync(StartupTaskState.DisabledByUser);

        Assert.IsFalse(vm.StartupToggleEnabled, "A user-controlled startup task cannot be toggled from the app.");
        StringAssert.Contains(vm.StartupStateText, "시작 프로그램 설정");

        await vm.ToggleStartupCommand.ExecuteAsync(null);
        Assert.AreEqual(0, hooks.Startup.EnableCalls, "The app must never retry an enable for a user-disabled task.");
    }

    [TestMethod]
    public async Task Test_Startup_toggle_disabled_by_policy_stays_disabled_and_reports_policy()
    {
        (SettingsViewModel vm, SettingsViewModelTestHooks hooks) = await CreateAsync(StartupTaskState.DisabledByPolicy);

        Assert.IsFalse(vm.StartupToggleEnabled);
        StringAssert.Contains(vm.StartupStateText, "정책");

        await vm.ToggleStartupCommand.ExecuteAsync(null);
        Assert.AreEqual(0, hooks.Startup.EnableCalls);
    }

    [TestMethod]
    public async Task Test_Startup_toggle_enables_when_permitted()
    {
        (SettingsViewModel vm, SettingsViewModelTestHooks hooks) = await CreateAsync(StartupTaskState.Disabled);

        await vm.ToggleStartupCommand.ExecuteAsync(null);

        Assert.AreEqual(1, hooks.Startup.EnableCalls);
        Assert.AreEqual(StartupTaskState.Enabled, vm.StartupState);
        Assert.IsTrue(vm.StartupIsOn);
    }

    [TestMethod]
    public async Task Test_Loads_and_registers_the_default_summon_hotkey()
    {
        (SettingsViewModel vm, SettingsViewModelTestHooks hooks) = await CreateAsync(StartupTaskState.Disabled);

        Assert.AreEqual(ShortcutSettings.SummonHotkeyDefault, vm.SummonHotkeyDisplay);
        Assert.AreEqual(1, hooks.Hotkeys.SetCalls.Count, "The persisted (default) hotkey is registered on load.");
        Assert.AreEqual(ShortcutSettings.SummonHotkeyDefault, hooks.Hotkeys.Current?.ToDisplayString());
    }

    [TestMethod]
    public async Task Test_StartCapture_enters_capture_mode_with_prompt()
    {
        (SettingsViewModel vm, _) = await CreateAsync(StartupTaskState.Disabled);

        vm.StartHotkeyCaptureCommand.Execute(null);

        Assert.IsTrue(vm.IsCapturingHotkey);
        Assert.AreEqual(Daynote.App.Localization.AppStrings.HotkeyCapturing, vm.HotkeyStatusText);
    }

    [TestMethod]
    public async Task Test_Applying_a_new_hotkey_registers_persists_and_exits_capture()
    {
        (SettingsViewModel vm, SettingsViewModelTestHooks hooks) = await CreateAsync(StartupTaskState.Disabled);
        vm.StartHotkeyCaptureCommand.Execute(null);

        await vm.HandleCapturedChordAsync(ModifierKeys.Control | ModifierKeys.Alt, Key.K);

        Assert.AreEqual("Ctrl+Alt+K", vm.SummonHotkeyDisplay);
        Assert.IsFalse(vm.IsCapturingHotkey);
        Assert.IsNull(vm.HotkeyStatusText);
        Assert.AreEqual("Ctrl+Alt+K", hooks.Hotkeys.Current?.ToDisplayString());
        Assert.AreEqual("Ctrl+Alt+K", hooks.Settings.Backing[ShortcutSettings.SummonHotkeyKey]);
    }

    [TestMethod]
    public async Task Test_A_conflicting_hotkey_keeps_the_previous_and_shows_status()
    {
        (SettingsViewModel vm, SettingsViewModelTestHooks hooks) = await CreateAsync(StartupTaskState.Disabled);
        hooks.Hotkeys.NextResult = Daynote.App.Input.HotkeySetResult.Conflict;
        vm.StartHotkeyCaptureCommand.Execute(null);

        await vm.HandleCapturedChordAsync(ModifierKeys.Control | ModifierKeys.Alt, Key.K);

        Assert.AreEqual(Daynote.App.Localization.AppStrings.HotkeyConflict, vm.HotkeyStatusText);
        Assert.AreEqual(ShortcutSettings.SummonHotkeyDefault, vm.SummonHotkeyDisplay, "The display keeps the working hotkey.");
        Assert.IsFalse(hooks.Settings.Backing.ContainsKey(ShortcutSettings.SummonHotkeyKey), "A conflict is not persisted.");
    }

    [TestMethod]
    public async Task Test_Reset_restores_the_default_summon_hotkey()
    {
        (SettingsViewModel vm, _) = await CreateAsync(StartupTaskState.Disabled);
        vm.StartHotkeyCaptureCommand.Execute(null);
        await vm.HandleCapturedChordAsync(ModifierKeys.Control | ModifierKeys.Alt, Key.K);
        Assert.AreEqual("Ctrl+Alt+K", vm.SummonHotkeyDisplay);

        await vm.ResetSummonHotkeyCommand.ExecuteAsync(null);

        Assert.AreEqual(ShortcutSettings.SummonHotkeyDefault, vm.SummonHotkeyDisplay);
    }

    [TestMethod]
    public async Task Test_Backup_flushes_then_writes_to_the_chosen_path()
    {
        (SettingsViewModel vm, SettingsViewModelTestHooks hooks) = await CreateAsync(StartupTaskState.Disabled);
        hooks.Picker.SavePath = @"D:\backups\daynote.zip";

        await vm.BackupCommand.ExecuteAsync(null);

        CollectionAssert.AreEqual(new[] { @"D:\backups\daynote.zip" }, hooks.Backup.BackupCalls);
        Assert.AreEqual(Daynote.App.Localization.AppStrings.BackupSucceeded, vm.BackupStatusText);
    }

    [TestMethod]
    public async Task Test_Backup_cancelled_picker_does_nothing()
    {
        (SettingsViewModel vm, SettingsViewModelTestHooks hooks) = await CreateAsync(StartupTaskState.Disabled);
        hooks.Picker.SavePath = null; // user cancelled the save dialog

        await vm.BackupCommand.ExecuteAsync(null);

        Assert.AreEqual(0, hooks.Backup.BackupCalls.Count);
        Assert.IsNull(vm.BackupStatusText);
    }

    [TestMethod]
    public async Task Test_Backup_blocked_when_flush_fails_and_never_writes()
    {
        (SettingsViewModel vm, SettingsViewModelTestHooks hooks) = await CreateAsync(StartupTaskState.Disabled, flushSucceeds: false);
        hooks.Picker.SavePath = @"D:\backups\daynote.zip";

        await vm.BackupCommand.ExecuteAsync(null);

        Assert.AreEqual(0, hooks.Backup.BackupCalls.Count, "A blocked flush must not produce a backup.");
        Assert.AreEqual(Daynote.App.Localization.AppStrings.BackupFlushBlocked, vm.BackupStatusText);
    }

    [TestMethod]
    public async Task Test_Restore_staged_requests_restart()
    {
        (SettingsViewModel vm, SettingsViewModelTestHooks hooks) = await CreateAsync(StartupTaskState.Disabled);
        hooks.Picker.OpenPath = @"D:\backups\daynote.zip";
        hooks.Backup.NextRestoreResult = Daynote.Core.Backup.RestoreStageResult.Staged();

        await vm.RestoreCommand.ExecuteAsync(null);

        CollectionAssert.AreEqual(new[] { @"D:\backups\daynote.zip" }, hooks.Backup.RestoreCalls);
        Assert.AreEqual(1, hooks.RestartRequests, "A staged restore triggers the restart.");
        Assert.AreEqual(Daynote.App.Localization.AppStrings.RestoreStagedRestarting, vm.BackupStatusText);
    }

    [TestMethod]
    public async Task Test_Restore_incompatible_reports_and_does_not_restart()
    {
        (SettingsViewModel vm, SettingsViewModelTestHooks hooks) = await CreateAsync(StartupTaskState.Disabled);
        hooks.Picker.OpenPath = @"D:\backups\future.zip";
        hooks.Backup.NextRestoreResult = Daynote.Core.Backup.RestoreStageResult.Incompatible();

        await vm.RestoreCommand.ExecuteAsync(null);

        Assert.AreEqual(0, hooks.RestartRequests, "A rejected restore never restarts.");
        Assert.AreEqual(Daynote.App.Localization.AppStrings.RestoreIncompatible, vm.BackupStatusText);
    }

    [TestMethod]
    public async Task Test_InApp_shortcut_capture_reassigns_persists_and_rebinds()
    {
        (SettingsViewModel vm, SettingsViewModelTestHooks hooks) = await CreateAsync(StartupTaskState.Disabled);
        ShortcutRowViewModel row = vm.InAppShortcuts.Single(r => r.Id == AppShortcuts.NewNote);
        Assert.AreEqual("Ctrl+N", row.Display);

        row.StartCaptureCommand.Execute(null);
        Assert.IsTrue(vm.IsCapturing);
        await vm.HandleCapturedChordAsync(ModifierKeys.Control | ModifierKeys.Shift, Key.M);

        Assert.AreEqual("Ctrl+Shift+M", row.Display);
        Assert.IsFalse(row.IsCapturing);
        Assert.AreEqual("Ctrl+Shift+M", hooks.Shortcuts.Get(AppShortcuts.NewNote).ToDisplayString());
        Assert.AreEqual("Ctrl+Shift+M", hooks.Settings.Backing[ShortcutSettings.ActionKey(AppShortcuts.NewNote)]);
    }

    [TestMethod]
    public async Task Test_InApp_shortcut_conflict_keeps_the_previous_binding()
    {
        (SettingsViewModel vm, _) = await CreateAsync(StartupTaskState.Disabled);
        ShortcutRowViewModel newNote = vm.InAppShortcuts.Single(r => r.Id == AppShortcuts.NewNote);

        newNote.StartCaptureCommand.Execute(null);
        await vm.HandleCapturedChordAsync(ModifierKeys.Control, Key.T); // already used by go-today

        Assert.AreEqual(Daynote.App.Localization.AppStrings.HotkeyConflict, newNote.StatusText);
        Assert.AreEqual("Ctrl+N", newNote.Display, "A conflicting chord does not change the binding.");
    }

    [TestMethod]
    public async Task Test_ShowTutorial_invokes_the_callback()
    {
        (SettingsViewModel vm, SettingsViewModelTestHooks hooks) = await CreateAsync(StartupTaskState.Disabled);

        vm.ShowTutorialCommand.Execute(null);

        Assert.AreEqual(1, hooks.TutorialRequests);
    }

    [TestMethod]
    public async Task Test_Storage_location_and_privacy_text_are_exposed()
    {
        (SettingsViewModel vm, _) = await CreateAsync(StartupTaskState.Disabled);

        StringAssert.Contains(vm.StorageLocation, "Daynote");
        StringAssert.Contains(vm.PrivacyText, "평문");

        // This used to pin the phrase "전송하지 않습니다" - it was pinning the inaccuracy. What the
        // statement must actually carry is the no-telemetry promise, which is still true.
        StringAssert.Contains(vm.PrivacyText, "원격 측정이 없습니다");
    }

    [TestMethod]
    public async Task Test_Privacy_text_discloses_the_MCP_integration()
    {
        (SettingsViewModel vm, _) = await CreateAsync(StartupTaskState.Disabled);

        // The MCP server ships in every build, so its disclosure is never optional.
        StringAssert.Contains(vm.PrivacyText, "AI 연동(MCP)");
    }

    [TestMethod]
    public void Test_Privacy_text_mentions_uploading_only_when_this_build_can_sync()
    {
        string withSync = SettingsViewModel.ComposePrivacyText(cloudSyncAvailable: true);
        string withoutSync = SettingsViewModel.ComposePrivacyText(cloudSyncAvailable: false);

        StringAssert.Contains(withSync, "클라우드 동기화");
        Assert.IsFalse(
            withoutSync.Contains("클라우드 동기화", StringComparison.Ordinal),
            "a build with no sync endpoint makes no network calls, so it must not describe uploading");
    }

    [TestMethod]
    public void Test_Privacy_text_no_longer_claims_nothing_leaves_the_machine()
    {
        // The old wording ("does not encrypt, sync, or send over the network") was the actual defect:
        // both cloud sync and the MCP server can move note content off this PC.
        foreach (string text in new[]
        {
            SettingsViewModel.ComposePrivacyText(cloudSyncAvailable: true),
            SettingsViewModel.ComposePrivacyText(cloudSyncAvailable: false),
        })
        {
            Assert.IsFalse(
                text.Contains("네트워크로 전송하지 않습니다", StringComparison.Ordinal),
                "the privacy statement must not promise that nothing is ever sent: " + text);
        }
    }
}
