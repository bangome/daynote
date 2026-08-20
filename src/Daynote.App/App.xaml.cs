using System.Windows;
using Daynote.App.Composition;
using Daynote.App.Input;
using Daynote.App.Lifecycle;
using Daynote.App.Settings;
using Daynote.App.Shell;
using Daynote.App.Showcase;
using Daynote.Core.Diagnostics;
using Daynote.Core.Settings;
using Daynote.Core.Startup;
using Daynote.Infrastructure.Instance;
using Microsoft.Extensions.DependencyInjection;

namespace Daynote.App;

/// <summary>
/// Product application entry. Enforces a single primary process per user, merges the theme
/// dictionaries (High Contrast per DESIGN Section 2), builds the composition root, and installs the
/// resident tray/lifecycle behavior with an explicit shutdown model (plan Todo 10).
/// </summary>
public partial class App : System.Windows.Application
{
    private const string InstanceBaseName = "Daynote";

    private ServiceProvider? _provider;
    private SingleInstanceCoordinator? _singleInstance;
    private AppLifecycleCoordinator? _coordinator;
    private TrayIconService? _tray;
    private GlobalHotkeyService? _hotkeys;
    private bool _relaunchAfterExit;

    /// <summary>
    /// A restore was staged; quit (flushing) and mark for relaunch so the staged data is applied on the
    /// next startup. The relaunch happens in <see cref="OnExit"/> after the single-instance mutex is
    /// released, so the new process claims primary cleanly.
    /// </summary>
    private void RequestRestartForRestore()
    {
        _relaunchAfterExit = true;
        _ = _coordinator?.QuitAsync();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Closing the window hides to the tray; only an explicit Quit shuts the process down.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        ShowcaseResources.Load(this, SystemParameters.HighContrast);

        _singleInstance = SingleInstanceCoordinator.ForCurrentUser(InstanceBaseName);
        if (_singleInstance.Start() == SingleInstanceRole.Secondary)
        {
            // Another primary already owns this user's session: activate it and exit.
            _singleInstance.ActivatePrimaryAsync(TimeSpan.FromSeconds(2)).GetAwaiter().GetResult();
            _singleInstance.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _singleInstance = null;
            Shutdown(0);
            return;
        }

        var appOptions = DaynoteAppOptions.ForCurrentUser();

        // Apply a staged restore BEFORE the database opens (the live writer connection can't be reopened
        // in-process). We are the primary instance here, so the swap is race-free.
        Daynote.Infrastructure.Backup.PendingRestore.ApplyIfPresent(appOptions.DataRoot);

        var services = new ServiceCollection();
        services.AddDaynote(appOptions);
        _provider = services.BuildServiceProvider();

        // Settle the UI language before anything reads a string: view model constructors and the
        // window's {loc:Tr} bindings both resolve against whatever is active at that moment. Blocking
        // here costs one small SQLite read and keeps the first painted frame in the right language.
        Localization.LanguageStartup
            .ApplyAsync(_provider.GetRequiredService<ISettingsStore>())
            .AsTask()
            .GetAwaiter()
            .GetResult();

        var window = _provider.GetRequiredService<Shell.Product.ProductWindow>();
        MainWindow = window;

        // Merge the product theme (light default) before first paint so the shell renders styled; the
        // persisted theme is re-applied during InitializeAsync.
        _provider.GetRequiredService<Shell.Product.IThemeApplier>().Apply(false);

        var options = _provider.GetRequiredService<DaynoteAppOptions>();
        var settingsStore = _provider.GetRequiredService<ISettingsStore>();
        var startupService = _provider.GetRequiredService<IStartupTaskService>();

        _tray = new TrayIconService();
        _coordinator = new AppLifecycleCoordinator(
            _tray,
            window,
            new WpfApplicationExit(this, window),
            (reason, cancellationToken) => window.ViewModel.Notes.FlushAsync(reason, cancellationToken),
            NullSanitizedLog.Instance,
            _singleInstance);

        window.AttachLifecycle(_coordinator);

        // The summon hotkey brings the window back from the tray; register it against the window handle
        // (which survives hide-to-tray) before Show so OnSourceInitialized can attach it.
        _hotkeys = new GlobalHotkeyService();
        _hotkeys.Pressed += (_, _) => _coordinator?.ShowWindow();
        _hotkeys.QuickNotePressed += (_, _) => _ = window.OpenQuickStickyNoteAsync();
        window.AttachHotkeys(_hotkeys);

        // Configurable in-app shortcuts: the window builds its KeyBindings from this set (and rebuilds
        // when the user reassigns one in Settings).
        var shortcuts = _provider.GetRequiredService<Input.ConfigurableShortcuts>();
        window.AttachShortcuts(shortcuts);

        // First-run onboarding tutorial (auto-shown once; re-openable from Settings).
        var tutorial = _provider.GetRequiredService<Onboarding.TutorialViewModel>();
        window.ViewModel.Tutorial = tutorial;

        var backupService = _provider.GetRequiredService<Daynote.Core.Backup.IBackupService>();
        var backupPicker = _provider.GetRequiredService<Settings.IBackupFilePicker>();
        window.ViewModel.SettingsViewModel = new SettingsViewModel(
            startupService, _hotkeys, settingsStore,
            backupService, backupPicker, shortcuts,
            async () => (await window.ViewModel.Notes.FlushAsync(Daynote.Core.Notes.FlushReason.Quit).ConfigureAwait(true)).CanProceed,
            RequestRestartForRestore,
            () => { window.ViewModel.CloseSettings(); tutorial.Open(); },
            options.DataRoot)
        {
            // Null unless this build has a sync endpoint, which keeps the whole section out of the
            // settings panel rather than showing a feature that cannot work.
            Account = _provider.GetService<Account.AccountViewModel>(),
        };
        if (window.ViewModel.SettingsViewModel.Account is { } account)
        {
            _ = account.InitializeAsync();
        }

        // Switching language rewrites the untouched first-run sample note into the new language too.
        Localization.LocalizationService.Instance.LanguageChanged += (_, _) => _ = RelocalizeSampleNoteAsync(window);

        window.Show();
        _ = InitializeAsync(window);
    }

    /// <summary>Re-localizes the untouched sample note on a language switch and reloads it if it's onscreen.</summary>
    private async Task RelocalizeSampleNoteAsync(Shell.Product.ProductWindow window)
    {
        if (_provider is null)
        {
            return;
        }

        try
        {
            var seed = new Daynote.Core.Notes.SeedSampleNote(
                _provider.GetRequiredService<Daynote.Core.Notes.INoteRepository>(),
                _provider.GetRequiredService<Daynote.Core.Settings.ISettingsStore>(),
                _provider.GetRequiredService<Func<Daynote.Core.Domain.Notes.NoteId>>());

            Daynote.Core.Domain.LocalDate? changed = await seed.RelocalizeAsync(
                Localization.AppStrings.SampleNoteTitle,
                date => string.Format(
                    System.Globalization.CultureInfo.CurrentCulture,
                    Localization.AppStrings.SampleNoteBodyFormat, date.Month, date.Day)).ConfigureAwait(true);

            // If the rewritten note is the date currently onscreen, flush pending edits then reload so
            // the editor shows the new-language text (the sample was untouched, so nothing is lost).
            if (changed is { } date && date == window.ViewModel.SelectedDate)
            {
                if ((await window.ViewModel.Notes.FlushAsync(Daynote.Core.Notes.FlushReason.DateChange).ConfigureAwait(true)).CanProceed)
                {
                    await window.ViewModel.Notes.LoadAsync(date).ConfigureAwait(true);
                }
            }
        }
        catch (Exception exception) when (exception is System.IO.IOException or InvalidOperationException)
        {
            // A best-effort convenience; never let it crash a language switch.
        }
    }

    private async Task InitializeAsync(Shell.Product.ProductWindow window)
    {
        // Seed a first-run sample note on today's date BEFORE the shell loads that date, so a brand-new
        // user opens onto a worked example. Runs once and never touches existing data.
        if (_provider is not null)
        {
            var clock = _provider.GetRequiredService<Daynote.Core.Time.IClock>();
            Daynote.Core.Domain.LocalDate today = Composition.LocalDates.Today(clock);
            var seed = new Daynote.Core.Notes.SeedSampleNote(
                _provider.GetRequiredService<Daynote.Core.Notes.INoteRepository>(),
                _provider.GetRequiredService<Daynote.Core.Settings.ISettingsStore>(),
                _provider.GetRequiredService<Func<Daynote.Core.Domain.Notes.NoteId>>());
            string body = string.Format(
                System.Globalization.CultureInfo.CurrentCulture,
                Localization.AppStrings.SampleNoteBodyFormat, today.Month, today.Day);
            await seed.ExecuteAsync(today, Localization.AppStrings.SampleNoteTitle, body).ConfigureAwait(true);
        }

        await window.ViewModel.InitializeAsync().ConfigureAwait(true);

        // Apply persisted in-app shortcut overrides (rebuilds the window's KeyBindings).
        if (_provider is not null)
        {
            await _provider.GetRequiredService<Input.ConfigurableShortcuts>().LoadAsync().ConfigureAwait(true);
        }

        // "Start with Windows" is opt-in: the app never auto-enables the startup task (Store policy);
        // the user turns it on from Settings. The manifest declares the task Enabled="false".
        if (window.ViewModel.SettingsViewModel is { } settings)
        {
            await settings.LoadAsync().ConfigureAwait(true);
        }

        Onboarding.TutorialViewModel? tutorial = window.ViewModel.Tutorial;
        if (tutorial is not null)
        {
            await tutorial.LoadAsync().ConfigureAwait(true);

            // First run opens straight onto the window; auto-show the onboarding tutorial now.
            if (tutorial.ShouldAutoShow)
            {
                tutorial.Open();
            }
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_coordinator is not null)
        {
            _coordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        _hotkeys?.Dispose();
        _tray?.Dispose();

        if (_singleInstance is not null)
        {
            _singleInstance.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        if (_provider is not null)
        {
            _provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        // Relaunch AFTER the mutex/pipe and db are released so the new instance becomes primary and its
        // startup applies the staged restore before opening the database.
        if (_relaunchAfterExit && Environment.ProcessPath is { } exePath)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exePath) { UseShellExecute = false });
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // If relaunch fails the restore still applies when the user next opens Daynote.
            }
        }

        base.OnExit(e);
    }

    /// <summary>Bridges the coordinator's shutdown request to WPF explicit shutdown.</summary>
    private sealed class WpfApplicationExit : IApplicationExit
    {
        private readonly System.Windows.Application _app;
        private readonly Shell.Product.ProductWindow _window;

        public WpfApplicationExit(System.Windows.Application app, Shell.Product.ProductWindow window)
        {
            _app = app;
            _window = window;
        }

        public void Shutdown()
        {
            _window.IsClosingToShutdown = true;
            _app.Shutdown(0);
        }
    }
}
