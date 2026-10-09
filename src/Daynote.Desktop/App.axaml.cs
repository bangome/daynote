using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using Daynote.App.Composition;
using Daynote.App.Localization;
using Daynote.Core.Notes;
using Daynote.Core.Settings;
using Daynote.Desktop.Composition;
using Daynote.Desktop.Lifecycle;
using Daynote.Desktop.ViewModels;
using Daynote.Desktop.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Daynote.Desktop;

/// <summary>
/// The Avalonia application: builds the composition root, settles the UI language, shows the shell,
/// and installs the resident behaviour (status-bar icon, hide on close, explicit Quit that flushes).
/// </summary>
public partial class App : Application
{
    private ServiceProvider? _provider;
    private ResidentLifecycle? _lifecycle;
    private MenuBarController? _menuBar;
    private Platform.MacWidgetBridge? _widgets;

    /// <summary>Set when a restore was staged: Program relaunches the process after the lifetime ends.</summary>
    internal static bool RelaunchAfterExit { get; private set; }

    private void RequestRestartForRestore()
    {
        RelaunchAfterExit = true;
        _ = _lifecycle?.QuitAsync();
    }

    /// <summary>
    /// The account panel moved the device to another profile (docs/PROFILES.md §8). Every service
    /// holds the old profile's database, so the app relaunches over the new one, exactly the way a
    /// staged restore does: Quit flushes the editor first, and the next start reads the pointer.
    /// </summary>
    /// <remarks>
    /// The panel already saved the editor before it moved the pointer, so Quit's own flush should
    /// find nothing to do. If it is refused anyway, the relaunch is disarmed and the panel is told,
    /// rather than leaving a process that believes it is quitting over a pointer that has moved.
    /// </remarks>
    private async void OnProfileSwitchRequested(object? sender, EventArgs e)
    {
        RelaunchAfterExit = true;
        if (_lifecycle is { } lifecycle && !await lifecycle.QuitAsync().ConfigureAwait(true))
        {
            RelaunchAfterExit = false;
            (sender as Daynote.App.Account.AccountViewModel)?.NotifyProfileSwitchFailed();
        }
    }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        // Closing the window hides it; only an explicit Quit ends the process.
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Migrates to the per-account layout on first run and resolves the active profile
        // (docs/PROFILES.md); everything below, restore included, acts on that profile's folder.
        var options = DaynoteAppOptions.ForCurrentUser();

        // A staged restore is applied before the database opens; we are the primary instance here.
        Infrastructure.Backup.PendingRestore.ApplyIfPresent(options.DataRoot);

        var services = new ServiceCollection();
        services.AddDaynoteDesktop(options, this, () => desktop.MainWindow, RequestRestartForRestore);
        _provider = services.BuildServiceProvider();

        // One small SQLite read so the first frame is already in the right language.
        LanguageStartup.ApplyAsync(_provider.GetRequiredService<ISettingsStore>())
            .AsTask().GetAwaiter().GetResult();

        DesktopShellViewModel shell = _provider.GetRequiredService<DesktopShellViewModel>();
        var window = new MainWindow { DataContext = shell };
        desktop.MainWindow = window;

        // Wired before anything in start-up can fail, so a profile switch is always carried out. The
        // panel saves the editor through this before it leaves a profile (docs/PROFILES.md §8).
        if (shell.Account is { } account)
        {
            account.FlushEditor = async () =>
            {
                await shell.Ticks.CommitAll().ConfigureAwait(true);
                return (await shell.Notes.FlushAsync(FlushReason.Quit).ConfigureAwait(true)).CanProceed;
            };
            account.ProfileSwitchRequested += OnProfileSwitchRequested;
        }

        _lifecycle = new ResidentLifecycle(
            this,
            desktop,
            window,
            LoadTrayIcon(),
            // A tick still held for its 600 ms (motion spec M3) is written before anything closes.
            async (reason, token) =>
            {
                await shell.Ticks.CommitAll().ConfigureAwait(true);
                return await shell.Notes.FlushAsync(reason, token).ConfigureAwait(true);
            },
            Program.SingleInstance);

        // Global chords: the summon key restores the window; ⌥` creates today's note as a post-it.
        var hotkeys = _provider.GetRequiredService<Daynote.App.Input.IGlobalHotkeyService>();
        hotkeys.Pressed += (_, _) => _lifecycle?.ShowWindow();
        hotkeys.QuickNotePressed += (_, _) =>
        {
            _lifecycle?.ShowWindow();
            _ = shell.OpenQuickStickyNoteAsync();
        };

        window.AttachShortcuts(_provider.GetRequiredService<Daynote.App.Input.ConfigurableShortcuts>());

        // The menu bar status item (Mac) or the tray flyout (Windows): today's count and quick capture.
        _menuBar = CreateMenuBar(shell, hotkeys);

        // Quit tears the resident lifecycle down and ends the lifetime; the status item goes with it.
        desktop.Exit += (_, _) => _menuBar?.Dispose();

        // The desktops read the motion spec's Mac/Win values, with reduced motion from the OS.
        var motion = new Platform.DesktopMotionPlatform();
        Daynote.Motion.MotionEnvironment.Flavor = Daynote.Motion.MotionFlavor.Desktop;
        Daynote.Motion.MotionEnvironment.Platform = motion;

        // The desktop widgets, when this bundle was built with them (native/mac).
        _widgets = Platform.MacWidgetBridge.TryAttach(
            this,
            shell,
            _provider.GetRequiredService<Core.Agenda.IAgendaRepository>(),
            _provider.GetRequiredService<INoteRepository>(),
            _provider.GetRequiredService<Core.Time.IClock>(),
            () => _lifecycle?.ShowWindow());

        // Back from the tray, the Dock or another app: pick up what other devices wrote meanwhile,
        // and a reduced-motion setting changed in System Settings meanwhile.
        window.Activated += (_, _) =>
        {
            shell.NotifyActivated();
            motion.Recheck();
            _widgets?.NotifyActivated();
        };

        window.Show();
        _ = InitializeAsync(shell);

        base.OnFrameworkInitializationCompleted();
    }

    private async Task InitializeAsync(DesktopShellViewModel shell)
    {
        if (_provider is null)
        {
            return;
        }

        try
        {
            // First-run sample note on today's date, before the shell loads that date (same as WPF).
            var clock = _provider.GetRequiredService<Core.Time.IClock>();
            Core.Domain.LocalDate today = LocalDates.Today(clock);
            var seed = new SeedSampleNote(
                _provider.GetRequiredService<INoteRepository>(),
                _provider.GetRequiredService<ISettingsStore>(),
                _provider.GetRequiredService<Func<Core.Domain.Notes.NoteId>>());
            string body = string.Format(
                System.Globalization.CultureInfo.CurrentCulture, AppStrings.SampleNoteBodyFormat, today.Month, today.Day);
            await seed.ExecuteAsync(today, AppStrings.SampleNoteTitle, body).ConfigureAwait(true);

            // An account profile's first start imports the notes a Move brought along; before the
            // day is read, so they are on screen from the first frame (docs/PROFILES.md §5.2).
            if (shell.Account is { } profileAccount)
            {
                await profileAccount.PrepareProfileAsync().ConfigureAwait(true);
            }

            await shell.InitializeAsync().ConfigureAwait(true);
            await _provider.GetRequiredService<Daynote.App.Input.ConfigurableShortcuts>().LoadAsync().ConfigureAwait(true);
            if (shell.SettingsViewModel is { } settings)
            {
                await settings.LoadSummonHotkeyAsync().ConfigureAwait(true);
                await settings.LoadMenuBarAsync().ConfigureAwait(true);
            }

            if (_menuBar is { } menuBar)
            {
                await menuBar.InitializeAsync().ConfigureAwait(true);
            }

            if (shell.Account is { } account)
            {
                await account.InitializeAsync().ConfigureAwait(true);
            }

            shell.StartAutoSync();

            // Look for a newer build in the background. Deliberately not awaited and never surfaced:
            // it downloads, stages, and the next start runs it. Nothing about writing a note should
            // wait on, or be interrupted by, an update (Platform/WindowsUpdateService.cs).
            _ = _provider.GetRequiredService<Platform.IUpdateService>().CheckAndStageAsync();

            // First-run tutorial: shown once, then only from Settings.
            if (shell.Tutorial is { } tutorial)
            {
                await tutorial.LoadAsync().ConfigureAwait(true);
                if (tutorial.ShouldAutoShow)
                {
                    tutorial.Open();
                }
            }
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            // The window is up; the user can still navigate. Surfacing this properly is part of the
            // settings/diagnostics work in the next phase.
            System.Diagnostics.Trace.TraceError(exception.ToString());
        }
    }

    private MenuBarController? CreateMenuBar(DesktopShellViewModel shell, Daynote.App.Input.IGlobalHotkeyService hotkeys)
    {
        if (_provider is null || _lifecycle is not { } lifecycle)
        {
            return null;
        }

        var append = new AppendNoteLine(
            _provider.GetRequiredService<INoteRepository>(),
            _provider.GetRequiredService<Func<Core.Domain.Notes.NoteId>>());
        MenuBarController? controller = null;
        var model = new MenuBarViewModel(
            _provider.GetRequiredService<Core.Agenda.IAgendaRepository>(),
            _provider.GetRequiredService<Core.Time.IClock>(),
            (line, newNote) => shell.AppendLineToTodayAsync(append, line, newNote),
            date =>
            {
                controller?.HideNow();
                lifecycle.ShowWindow();
                if (date is { } day)
                {
                    MenuBarController.Forget(shell.ShowDateFromMenuBarAsync(day));
                }
            },
            () =>
            {
                controller?.HideNow();
                lifecycle.ShowWindow();
                shell.OpenSettingsFromMenuBar();
            });

        controller = new MenuBarController(this, model, lifecycle, hotkeys, shell.SettingsViewModel);

        // Two ways round: what the popover makes shows in the window's panels, and what the window
        // changes moves the count beside the status item.
        model.AgendaChanged += (_, _) => MenuBarController.Forget(shell.Todo.RefreshAsync());
        shell.Todo.Refreshed += (_, _) => controller.NotifyAgendaChanged();
        return controller;
    }

    private static WindowIcon LoadTrayIcon()
    {
        using Stream stream = AssetLoader.Open(new Uri("avares://Daynote.Desktop/Assets/daynote-favicon-v1.png"));
        return new WindowIcon(stream);
    }
}
