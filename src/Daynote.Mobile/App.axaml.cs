using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Daynote.App.Composition;
using Daynote.App.Localization;
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

        var options = new DaynoteAppOptions(platform.DataRoot)
        {
            SyncEndpoint = DaynoteAppOptions.ResolveSyncEndpoint(
                Environment.GetEnvironmentVariable(DaynoteAppOptions.SyncEndpointEnvironmentVariable)),
        };

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
        singleView.MainView = view;

        _ = _shell.InitializeAsync();

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Saves the open note. Each head calls this when the OS is about to background the app, which on
    /// a phone is the only reliable "we might not run again" signal there is.
    /// </summary>
    public Task FlushAsync() => _shell?.FlushAsync(FlushReason.Hide) ?? Task.CompletedTask;

    /// <summary>The system back gesture: closes the editor if it is up, else lets the OS have it.</summary>
    public Task<bool> TryGoBackAsync() => _shell?.CloseEditorAsync() ?? Task.FromResult(false);
}
