using Daynote.App.Composition;

namespace Daynote.App.Tests.Composition;

/// <summary>
/// Where the cloud sync endpoint comes from.
/// </summary>
/// <remarks>
/// These exist because of a real failure: the endpoint was read only from an environment variable,
/// which no installed build ever has, so <c>SyncEndpoint</c> was null everywhere and
/// <c>SyncRegistration</c> registered nothing — the account section was absent from the settings
/// panel of every shipped copy while the service itself was live and working. The first test below
/// is the one that would have caught it.
/// </remarks>
[TestClass]
public sealed class SyncEndpointTests
{
    [TestMethod]
    public void ResolveSyncEndpoint_WithNoOverride_UsesTheDeployedService()
    {
        Assert.AreEqual(
            new Uri(DaynoteAppOptions.DefaultSyncEndpoint),
            DaynoteAppOptions.ResolveSyncEndpoint(null));
        Assert.AreEqual(
            new Uri(DaynoteAppOptions.DefaultSyncEndpoint),
            DaynoteAppOptions.ResolveSyncEndpoint("   "));
    }

    [TestMethod]
    public void DefaultSyncEndpoint_IsHttps()
    {
        // The bearer token and the ciphertext must not cross a plaintext connection, and the default
        // is the one endpoint nobody re-checks.
        Assert.AreEqual(Uri.UriSchemeHttps, new Uri(DaynoteAppOptions.DefaultSyncEndpoint).Scheme);
    }

    [TestMethod]
    public void ResolveSyncEndpoint_WithOverride_PrefersIt()
    {
        Assert.AreEqual(
            new Uri("https://localhost:8787"),
            DaynoteAppOptions.ResolveSyncEndpoint("https://localhost:8787"));
    }

    [TestMethod]
    public void ResolveSyncEndpoint_WithOff_DisablesCloudSync()
    {
        // The escape hatch for a wholly offline build: null keeps the whole section out of the
        // settings panel rather than showing a feature that cannot work.
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
