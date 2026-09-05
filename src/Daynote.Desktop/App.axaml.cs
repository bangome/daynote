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

    /// <summary>Set when a restore was staged: Program relaunches the process after the lifetime ends.</summary>
    internal static bool RelaunchAfterExit { get; private set; }

    private void RequestRestartForRestore()
    {
        RelaunchAfterExit = true;
        _ = _lifecycle?.QuitAsync();
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

        _lifecycle = new ResidentLifecycle(
            this,
            desktop,
            window,
            LoadTrayIcon(),
            (reason, token) => shell.Notes.FlushAsync(reason, token),
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

            await shell.InitializeAsync().ConfigureAwait(true);
            await _provider.GetRequiredService<Daynote.App.Input.ConfigurableShortcuts>().LoadAsync().ConfigureAwait(true);
            if (shell.SettingsViewModel is { } settings)
            {
                await settings.LoadSummonHotkeyAsync().ConfigureAwait(true);
            }

            if (shell.Account is { } account)
            {
                await account.InitializeAsync().ConfigureAwait(true);
            }

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

    private static WindowIcon LoadTrayIcon()
    {
        using Stream stream = AssetLoader.Open(new Uri("avares://Daynote.Desktop/Assets/daynote-favicon-v1.png"));
        return new WindowIcon(stream);
    }
}
