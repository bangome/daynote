using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Daynote.App.Composition;
using Daynote.Mobile.Composition;
using Daynote.Mobile.Platform;
using Daynote.Mobile.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Daynote.Mobile.Tests;

/// <summary>The phone's real service graph, pointed at a throwaway data root.</summary>
internal static class TestServices
{
    /// <summary>
    /// Platform services as a build with no keystore and no sign-in would have them.
    /// </summary>
    /// <remarks>
    /// Both are null on purpose: it is the shape a head has before its OAuth client exists, so these
    /// tests cover the state the app actually ships in today (local-only, account section absent).
    /// A null secret protector also keeps the run from touching the machine's real keychain.
    /// </remarks>
    internal static MobilePlatformServices PlatformFor(string dataRoot) =>
        new(dataRoot, SecretProtector: null, Identity: null, OpenExternal: _ => { }, TopLevel: () => null);

    internal static ServiceProvider Build(string dataRoot, Application application)
    {
        var services = new ServiceCollection();
        services.AddDaynoteMobile(
            new DaynoteAppOptions(dataRoot),
            application,
            () => null,
            PlatformFor(dataRoot));
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// The phone shell, laid out at a handset size and initialised the way App.axaml.cs does it.
    /// </summary>
    /// <remarks>
    /// Without <c>InitializeAsync</c> the calendar has a weekday header and no days. Its
    /// continuations post to this dispatcher, so the loop pumps until the task completes and then
    /// drains what the initialisation queued for after itself, leaving the settled UI.
    /// <para>
    /// 390x844 is the iPhone 14/15/16 logical size and the narrowest mainstream handset; anything
    /// that fits here fits the Android field too.
    /// </para>
    /// </remarks>
    internal static void WithInitialisedShell(Action<Views.MainView, MobileShellViewModel> body)
    {
        ArgumentNullException.ThrowIfNull(body);
        using var data = new TempDataRoot();

        HeadlessAppFixture.OnUiThread(() =>
        {
            Application application = Application.Current!;
            ServiceProvider provider = Build(data.Path, application);
            var shell = provider.GetRequiredService<MobileShellViewModel>();
            var view = new Views.MainView { DataContext = shell };

            // Hosted in a window because Avalonia only applies styles and control templates inside a
            // tree rooted at a TopLevel; a detached UserControl measures to nothing. On a device the
            // single-view lifetime is that root, and headless has no single-view host, so this
            // stands in for it at handset size.
            var host = new Window { Content = view, Width = 390, Height = 844 };
            host.Show();

            Task initialising = shell.InitializeAsync();
            DateTime deadline = DateTime.UtcNow.AddSeconds(20);
            while (!initialising.IsCompleted)
            {
                Assert.IsTrue(DateTime.UtcNow < deadline, "The shell did not finish initialising within 20 seconds.");
                Dispatcher.UIThread.RunJobs();
                Thread.Sleep(5);
            }

            initialising.GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();

            host.UpdateLayout();

            // Not disposed: the shell is IAsyncDisposable only, and a synchronous container dispose
            // throws on it. The data root goes away with TempDataRoot either way.
            try
            {
                body(view, shell);
            }
            finally
            {
                host.Close();
            }
        });
    }
}

/// <summary>A disposable data root, so a run never touches the operator's own notes.</summary>
internal sealed class TempDataRoot : IDisposable
{
    internal string Path { get; } = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(), "daynote-mobile-tests", Guid.NewGuid().ToString("N"));

    internal TempDataRoot() => Directory.CreateDirectory(Path);

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A SQLite handle can outlive the test by a moment; a leftover temp folder is not a
            // failure worth turning a green run red.
        }
    }
}
