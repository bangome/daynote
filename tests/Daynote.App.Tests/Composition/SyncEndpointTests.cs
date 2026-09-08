using Daynote.App.Composition;

namespace Daynote.App.Tests.Composition;

/// <summary>
/// Where the cloud sync endpoint comes from, and whether a shipped build has one.
/// </summary>
/// <remarks>
/// These exist because of a real failure: the endpoint was read only from an environment variable,
/// which no installed build has, so <c>SyncEndpoint</c> was null everywhere and
/// <c>SyncRegistration</c> registered nothing — the account section was absent from the settings
/// panel of every shipped copy while the service itself was live and working. Cloud sync is now off
/// on purpose rather than by accident, and the tests below pin which of those two it is.
/// </remarks>
[TestClass]
public sealed class SyncEndpointTests
{
    [TestMethod]
    public void CloudSync_Ships()
    {
        // Not a style rule — this is the release decision, and it is the one line that changes when
        // it is revisited. It was held back on the grounds that password-reset mail was unverified,
        // which stopped applying when sign-in became Google's: there is no password and no reset
        // route. Turned on 2026-09-08.
        Assert.IsTrue(
            DaynoteAppOptions.SyncEnabledByDefault,
            "Cloud sync ships; a build that turns it back off should say why here.");
    }

    [TestMethod]
    public void The_package_declares_the_network_cloud_sync_uses()
    {
        // The two have to move together. An MSIX blocks outbound calls it has not declared, so a
        // package with the account UI and no internetClient would show a sign-in that cannot reach
        // anything — and the Store lists declared capabilities, so an undeclared network is also an
        // undisclosed one.
        string manifest = File.ReadAllText(Path.Combine(
            RepositoryRoot, "packaging", "Daynote.Package", "Package.appxmanifest"));

        Assert.AreEqual(
            DaynoteAppOptions.SyncEnabledByDefault,
            manifest.Contains("Name=\"internetClient\"", StringComparison.Ordinal),
            "Package.appxmanifest and SyncEnabledByDefault disagree about whether this build "
                + "talks to a server.");
    }

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DESIGN.md")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException($"No Daynote repository above '{AppContext.BaseDirectory}'.");
    }

    [TestMethod]
    public void ResolveSyncEndpoint_WithNoOverride_MatchesWhatThisBuildShips()
    {
        Uri? resolved = DaynoteAppOptions.ResolveSyncEndpoint(null);

        if (DaynoteAppOptions.SyncEnabledByDefault)
        {
            Assert.AreEqual(new Uri(DaynoteAppOptions.DeployedSyncEndpoint), resolved);
        }
        else
        {
            // Null is what keeps the account section, the HttpClient, and every network call out of
            // the build. A "disabled" endpoint that still resolved would ship the UI regardless.
            Assert.IsNull(resolved, "A build with cloud sync off must resolve no endpoint at all.");
            Assert.IsNull(DaynoteAppOptions.ResolveSyncEndpoint("   "));
        }
    }

    [TestMethod]
    public void DeployedSyncEndpoint_IsHttps()
    {
        // The bearer token and the ciphertext must not cross a plaintext connection, and the shipped
        // default is the one endpoint nobody re-checks.
        Assert.AreEqual(Uri.UriSchemeHttps, new Uri(DaynoteAppOptions.DeployedSyncEndpoint).Scheme);
    }

    [TestMethod]
    public void ResolveSyncEndpoint_WithAnOverride_TurnsCloudSyncOn()
    {
        // How development and QA reach the feature while it is held back.
        Assert.AreEqual(
            new Uri("https://localhost:8787"),
            DaynoteAppOptions.ResolveSyncEndpoint("https://localhost:8787"));
        Assert.AreEqual(
            new Uri(DaynoteAppOptions.DeployedSyncEndpoint),
            DaynoteAppOptions.ResolveSyncEndpoint(DaynoteAppOptions.DeployedSyncEndpoint));
    }

    [TestMethod]
    public void ResolveSyncEndpoint_WithOff_ForcesCloudSyncOff()
    {
        // Still meaningful with the flag false: it is what a build keeps once the flag flips.
        Assert.IsNull(DaynoteAppOptions.ResolveSyncEndpoint("off"));
        Assert.IsNull(DaynoteAppOptions.ResolveSyncEndpoint("OFF"));
    }

    [TestMethod]
    public void ResolveSyncEndpoint_WithPlaintextOrNonsense_DisablesRatherThanDowngrades()
    {
        Assert.IsNull(DaynoteAppOptions.ResolveSyncEndpoint("http://daynote.arachat.cc"));
        Assert.IsNull(DaynoteAppOptions.ResolveSyncEndpoint("daynote.arachat.cc"));
    }
}
