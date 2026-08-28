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
    public void CloudSync_IsNotShippedYet()
    {
        // Not a style rule — this is the release decision, and it is the one line that changes when
        // the feature ships. If someone flips the flag, the two tests below flip with it and say so.
        Assert.IsFalse(
            DaynoteAppOptions.SyncEnabledByDefault,
            "Cloud sync is held back until password-reset mail is verified end to end.");
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
