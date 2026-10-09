using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Daynote.App.Composition;
using Daynote.App.Localization;
using Daynote.Core.Domain;
using Daynote.Core.Notes;
using Daynote.Core.Settings;
using Daynote.Mobile.Composition;
using Daynote.Mobile.Platform;
using Daynote.Mobile.ViewModels;
using Daynote.Mobile.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Daynote.Mobile;

/// <summary>
/// The Avalonia application shared by both phone heads: builds the composition root, settles the UI
/// language, and shows the shell inside whatever single-view lifetime the head provides.
/// </summary>
/// <remarks>
/// A phone app has a single view, not a window, so the lifetime is
/// <see cref="ISingleViewApplicationLifetime"/> rather than the desktop's classic one. There is no
/// "quit": the OS suspends and later kills the process, which is why <see cref="FlushAsync"/> exists
/// and why each head calls it from its own pause callback.
/// </remarks>
public partial class App : Application
{
    private ServiceProvider? _provider;
    private MobileShellViewModel? _shell;
    /// <summary>
    /// The lifetime's one main view, set once. A switch swaps what is inside it: on Android a later
    /// <c>MainView</c> assignment does not reach the screen, which kept the old shell up after a switch.
    /// </summary>
    private readonly ContentControl _host = new();

    /// <summary>The profile folder the current composition runs on, to fall back to if a switch cannot open its target.</summary>
    private string? _activeFolder;

    /// <summary>
    /// Supplied by the head before <c>Initialize</c>: everything platform-shaped this app needs.
    /// </summary>
    public static MobilePlatformServices? Platform { get; set; }

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not ISingleViewApplicationLifetime singleView)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        MobilePlatformServices platform = Platform
            ?? throw new InvalidOperationException("App.Platform must be set by the head before the app starts.");

        // The phones read the spec's touch values; the head reports reduced motion and plays haptics.
        Daynote.Motion.MotionEnvironment.Flavor = Daynote.Motion.MotionFlavor.Touch;
        Daynote.Motion.MotionEnvironment.Platform = platform.Motion;

        singleView.MainView = _host;
        Compose(platform);

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Builds the composition over the active profile and gives the single view a new shell. Runs at
    /// start and again after a profile switch.
    /// </summary>
    private void Compose(MobilePlatformServices platform)
    {
        // The sandbox folder is the base root; the app runs over the active profile inside it, after
        // the one-time profile migration (docs/PROFILES.md §5.1). After a switch this is also what
        // removes an account folder the user asked to remove, now that nothing holds it open.
        var options = DaynoteAppOptions.ForBaseRoot(platform.DataRoot);
        _activeFolder = options.DataRoot;

        var view = new MainView();

        var services = new ServiceCollection();
        services.AddDaynoteMobile(options, this, () => TopLevel.GetTopLevel(view), platform);
        _provider = services.BuildServiceProvider();

        // One small SQLite read so the first frame is already in the right language.
        LanguageStartup.ApplyAsync(_provider.GetRequiredService<ISettingsStore>())
            .GetAwaiter()
            .GetResult();

        _shell = _provider.GetRequiredService<MobileShellViewModel>();
        view.DataContext = _shell;
        _host.Content = view;

        if (_shell.Account is { } account)
        {
            MobileShellViewModel shell = _shell;
            account.FlushEditor = async () => (await shell.FlushAsync(FlushReason.Quit).ConfigureAwait(true)).CanProceed;
            account.ProfileSwitchRequested += OnProfileSwitchRequested;

            // Locked or unlocked here or by another device: the widgets show the day, or nothing.
            account.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(Daynote.App.Account.AccountViewModel.IsLocked))
                {
                    platform.AgendaChanged?.Invoke();
                }
            };
        }

        _ = StartAsync(_shell);
    }

    /// <summary>
    /// The account card moved the device to another profile (docs/PROFILES.md §8). A phone app cannot
    /// relaunch itself, so the composition is rebuilt in place: flush the open note, dispose the old
    /// provider — which closes the old profile's database — and compose again over the new one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A flush that fails stops here, before anything is disposed: the card is told and the app keeps
    /// running on the profile it has, with the pointer already on the new one for the next start.
    /// </para>
    /// <para>
    /// Once the old provider is gone there is no screen to go back to, so a new profile that will not
    /// open is not left as a dead view: the pointer goes back to the profile that was running and that
    /// is composed again, and if even that fails the view says so plainly.
    /// </para>
    /// </remarks>
    private async void OnProfileSwitchRequested(object? sender, EventArgs e)
    {
        var account = sender as Daynote.App.Account.AccountViewModel;
        MobilePlatformServices platform = Platform!;
        string? previousFolder = _activeFolder;
        try
        {
            if (_shell is { } shell && !(await shell.FlushAsync(FlushReason.Quit).ConfigureAwait(true)).CanProceed)
            {
                account?.NotifyProfileSwitchFailed();
                return;
            }

            if (account is not null)
            {
                account.ProfileSwitchRequested -= OnProfileSwitchRequested;
            }

            // The old profile's to-dos must not go on reminding under the new one. The new
            // composition's first pass would cancel them too (its diff runs against the same
            // device-level state), but not if the new profile fails to open.
            if (_provider?.GetService<Reminders.ReminderCoordinator>() is { } reminders)
            {
                await reminders.ClearAsync().ConfigureAwait(true);
            }

            if (_provider is { } provider)
            {
                _provider = null;
                _shell = null;
                await provider.DisposeAsync().ConfigureAwait(true);
            }
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException))
        {
            System.Diagnostics.Trace.TraceError(exception.ToString());
        }

        if (TryCompose(platform))
        {
            return;
        }

        if (previousFolder is not null && TryPointBackAt(platform.DataRoot, previousFolder) && TryCompose(platform))
        {
            return;
        }

        _host.Content = new TextBlock
        {
            Text = AppStrings.ProfileSwitchFailedScreen,
            TextWrapping = Avalonia.Media.TextWrapping.Wrap,
            TextAlignment = Avalonia.Media.TextAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(24),
        };
    }

    private bool TryCompose(MobilePlatformServices platform)
    {
        try
        {
            Compose(platform);
            return true;
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException))
        {
            System.Diagnostics.Trace.TraceError(exception.ToString());
            if (_provider is { } half)
            {
                _provider = null;
                _shell = null;
                half.Dispose();
            }

            return false;
        }
    }

    /// <summary>Points <c>profile.json</c> back at the profile whose folder the old composition ran on.</summary>
    private static bool TryPointBackAt(string baseRoot, string folder)
    {
        try
        {
            var profiles = new Daynote.Infrastructure.Persistence.Profiles.ProfileStore(baseRoot);
            string name = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
            profiles.SetActiveProfile(
                string.Equals(Path.GetFullPath(folder), profiles.BaseRoot, StringComparison.OrdinalIgnoreCase)
                    ? Daynote.Infrastructure.Persistence.Profiles.ProfileStore.LocalProfileId
                    : name);
            return true;
        }
        catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException))
        {
            System.Diagnostics.Trace.TraceError(exception.ToString());
            return false;
        }
    }

    /// <summary>
    /// Loads the day's notes, then reads back the stored sign-in.
    /// </summary>
    /// <remarks>
    /// The account read comes second on purpose, and it is the same order the desktop start-up uses:
    /// it touches the keystore and the settings row, and a note is worth showing before either. It is
    /// not optional, though - without it a signed-in user relaunching the app sees the sign-in button
    /// again, because nothing else ever reads the session back off disk. Automatic sync starts only
    /// after both, and in the background, so a launch still never waits on the network.
    /// </remarks>
    private static async Task StartAsync(MobileShellViewModel shell)
    {
        // An account profile's first start imports the notes a Move brought along, before the day is
        // read, so they are on screen from the start (docs/PROFILES.md §5.2).
        if (shell.Account is { } profileAccount)
        {
            await profileAccount.PrepareProfileAsync().ConfigureAwait(true);
        }

        await shell.InitializeAsync().ConfigureAwait(true);
        _initialised = shell;
        if (_pendingReminder is { } tapped)
        {
            _pendingReminder = null;
            await shell.OpenReminderAsync(tapped.Date, tapped.NoteId).ConfigureAwait(true);
        }
        else if (_pendingWidget is { } launch)
        {
            _pendingWidget = null;
            await shell.OpenFromWidgetAsync(launch).ConfigureAwait(true);
        }

        if (shell.Account is { } account)
        {
            await account.InitializeAsync().ConfigureAwait(true);
        }

        shell.StartAutoSync();
    }

    /// <summary>The shell whose day has loaded, so a tapped reminder can be handed straight to it.</summary>
    private static MobileShellViewModel? _initialised;

    /// <summary>A reminder tapped before the shell was ready, typically the one that launched the app.</summary>
    private static (LocalDate Date, Guid NoteId)? _pendingReminder;

    /// <summary>
    /// A to-do reminder was tapped. Each head calls this on the UI thread with the date and note it
    /// put into the notification; the app opens that day with the note in the editor, now if it is
    /// running and as soon as the day has loaded if the tap is what launched it.
    /// </summary>
    public static void OpenReminder(string? date, string? noteId)
    {
        if (LocalDate.Parse(date) is not { IsSuccess: true } day || !Guid.TryParse(noteId, out Guid note))
        {
            return;
        }

        if (Current is App { _shell: { } shell } && ReferenceEquals(shell, _initialised))
        {
            _ = shell.OpenReminderAsync(day.Value, note);
            return;
        }

        _pendingReminder = (day.Value, note);
    }

    /// <summary>A widget tap that arrived before the shell was ready, typically the one that launched the app.</summary>
    private static WidgetLaunch? _pendingWidget;

    /// <summary>
    /// A home-screen widget was tapped. The head calls this on the UI thread; as with a reminder,
    /// the app acts now if it is running and as soon as the day has loaded if the tap launched it.
    /// </summary>
    public static void OpenFromWidget(WidgetLaunch launch)
    {
        if (Current is App { _shell: { } shell } && ReferenceEquals(shell, _initialised))
        {
            _ = shell.OpenFromWidgetAsync(launch);
            return;
        }

        _pendingWidget = launch;
    }

    /// <summary>
    /// The running composition and its shell, once the day has loaded; null before that, after a
    /// failed switch, and when the process was started for a broadcast and has no UI. What the
    /// widgets read through instead of opening the database a second time.
    /// </summary>
    internal static (IServiceProvider Services, MobileShellViewModel Shell)? Live =>
        Current is App { _provider: { } provider, _shell: { } shell } && ReferenceEquals(shell, _initialised)
            ? (provider, shell)
            : null;

    /// <summary>
    /// The app came back to the foreground. Each head calls this; it is when a phone most likely
    /// has notes waiting from another device.
    /// </summary>
    public void NotifyResumed() => _shell?.NotifyResumed();

    /// <summary>
    /// Saves the open note. Each head calls this when the OS is about to background the app, which on
    /// a phone is the only reliable "we might not run again" signal there is.
    /// </summary>
    public Task FlushAsync() => _shell?.FlushAsync(FlushReason.Hide) ?? Task.CompletedTask;

    /// <summary>The system back gesture: closes the sheet, page or editor on top, else lets the OS have it.</summary>
    public Task<bool> TryGoBackAsync() => _shell?.GoBackAsync() ?? Task.FromResult(false);
}
