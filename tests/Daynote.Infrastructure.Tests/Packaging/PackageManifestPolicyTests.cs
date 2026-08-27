using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Daynote.Infrastructure.Tests.Packaging;

/// <summary>
/// Static (no-install) validation of the Daynote Microsoft Store MSIX manifest. These
/// tests never spawn a subprocess and never touch a cert store or an installed package;
/// they parse the committed Package.appxmanifest only.
/// </summary>
[TestClass]
public sealed class PackageManifestPolicyTests
{
    private static string ManifestPath =>
        Path.Combine(AppContext.BaseDirectory, "Packaging", "Package.appxmanifest");

    private static XDocument LoadManifest() => XDocument.Load(ManifestPath, LoadOptions.None);

    [TestMethod]
    public void Test_manifest_is_well_formed_xml_with_package_root()
    {
        Assert.IsTrue(File.Exists(ManifestPath), $"Manifest not copied to test output: {ManifestPath}");

        // Load throws on malformed XML; the assertion pins the expected root.
        XDocument document = LoadManifest();
        Assert.IsNotNull(document.Root);
        Assert.AreEqual("Package", document.Root!.Name.LocalName);
    }

    [TestMethod]
    public void Test_manifest_satisfies_every_packaging_policy()
    {
        XDocument document = LoadManifest();

        IReadOnlyList<string> violations = PackageManifestPolicy.Evaluate(document);

        Assert.AreEqual(
            0,
            violations.Count,
            "Manifest policy violations: " + string.Join(" | ", violations));
    }

    [TestMethod]
    public void Test_identity_is_x64_only_and_well_formed()
    {
        XDocument document = LoadManifest();
        XElement identity = document.Descendants()
            .Single(static element => element.Name.LocalName == "Identity");

        Assert.AreEqual(PackageManifestPolicy.ExpectedIdentityName, (string?)identity.Attribute("Name"));
        Assert.AreEqual(PackageManifestPolicy.ExpectedPublisher, (string?)identity.Attribute("Publisher"));
        // Bumped for every submission, so assert the Store's rule instead of today's number.
        StringAssert.Matches((string?)identity.Attribute("Version"), new Regex(@"^\d+\.\d+\.\d+\.0$"));
        Assert.AreEqual(PackageManifestPolicy.ExpectedArchitecture, (string?)identity.Attribute("ProcessorArchitecture"));

        // No x86 / Arm64 identity is present anywhere.
        bool hasForbiddenArchitecture = document.Descendants()
            .Where(static element => element.Name.LocalName == "Identity")
            .Select(static element => (string?)element.Attribute("ProcessorArchitecture"))
            .Any(static value => value is "x86" or "arm64" or "arm");
        Assert.IsFalse(hasForbiddenArchitecture, "No x86/Arm/Arm64 identity may be declared.");
    }

    [TestMethod]
    public void Test_minimum_build_matches_the_target_framework()
    {
        XDocument document = LoadManifest();
        XElement target = document.Descendants()
            .Single(static element => element.Name.LocalName == "TargetDeviceFamily");

        // Daynote.App TFM is net10.0-windows10.0.19041.0.
        Assert.AreEqual(PackageManifestPolicy.ExpectedMinVersion, (string?)target.Attribute("MinVersion"));
        Assert.AreEqual("Windows.Desktop", (string?)target.Attribute("Name"));
    }

    [TestMethod]
    public void Test_policy_rejects_a_manifest_that_disables_file_system_virtualization()
    {
        // The sideload builds disabled virtualization to keep the un-redirected data path across
        // uninstall. That needs a restricted capability the Store will not grant, so a manifest that
        // reintroduces it must be rejected here rather than by Partner Center.
        XDocument mutated = LoadManifest();
        XNamespace desktop6 = "http://schemas.microsoft.com/appx/manifest/desktop/windows10/6";
        XElement properties = mutated.Descendants()
            .Single(static element => element.Name.LocalName == "Properties");
        properties.Add(new XElement(desktop6 + "FileSystemWriteVirtualization", "disabled"));

        IReadOnlyList<string> violations = PackageManifestPolicy.Evaluate(mutated);

        Assert.IsTrue(
            violations.Any(static v => v.Contains("FileSystemWriteVirtualization", StringComparison.Ordinal)),
            "Disabling virtualization must be rejected. Got: " + string.Join(" | ", violations));
    }

    [TestMethod]
    public void Test_policy_rejects_a_manifest_that_declares_unvirtualized_resources()
    {
        // Same story from the capability side, through a disposable temp-file copy.
        string tempManifest = Path.Combine(
            Path.GetTempPath(),
            "daynote-manifest-policy",
            Guid.NewGuid().ToString("N"),
            "Package.appxmanifest");
        Directory.CreateDirectory(Path.GetDirectoryName(tempManifest)!);
        try
        {
            XDocument mutated = LoadManifest();
            XNamespace rescap = "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities";
            XElement capabilities = mutated.Descendants()
                .Single(static element => element.Name.LocalName == "Capabilities");
            capabilities.Add(new XElement(rescap + "Capability", new XAttribute("Name", "unvirtualizedResources")));
            mutated.Save(tempManifest);

            IReadOnlyList<string> violations = PackageManifestPolicy.Evaluate(XDocument.Load(tempManifest));

            Assert.IsTrue(
                violations.Any(static v => v.Contains("unvirtualizedResources", StringComparison.Ordinal)),
                "Declaring unvirtualizedResources must be rejected. Got: " + string.Join(" | ", violations));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(Path.GetDirectoryName(tempManifest)!)!, recursive: true);
        }
    }

    [TestMethod]
    public void Test_policy_rejects_a_manifest_whose_startup_task_is_enabled_by_default()
    {
        // Negative: a StartupTask enabled by default violates the opt-in contract.
        XDocument mutated = LoadManifest();
        XElement startupTask = mutated.Descendants()
            .Single(static element => element.Name.LocalName == "StartupTask");
        startupTask.SetAttributeValue("Enabled", "true");

        IReadOnlyList<string> violations = PackageManifestPolicy.Evaluate(mutated);

        Assert.IsTrue(
            violations.Any(static v => v.Contains("Enabled", StringComparison.Ordinal)),
            "An enabled-by-default StartupTask must be rejected. Got: " + string.Join(" | ", violations));
    }

    [TestMethod]
    public void Test_the_mcp_server_is_an_alias_on_the_app_not_an_app_of_its_own()
    {
        XDocument document = LoadManifest();

        // Partner Center refused an earlier build for exactly this: a second <Application> with
        // AppListEntry="none" is a headless app, which needs a HeadlessAppBypass entitlement. The
        // alias rides on the app's own entry instead, which also keeps a useless Start-menu tile off
        // a server that only speaks stdio.
        List<XElement> applications = document.Descendants()
            .Where(static element => element.Name.LocalName == "Application")
            .ToList();
        Assert.AreEqual(1, applications.Count, "the package must declare exactly one application");
        Assert.AreEqual(PackageManifestPolicy.ExpectedApplicationId, (string?)applications[0].Attribute("Id"));

        Assert.IsFalse(
            document.Descendants().Any(static element => element.Attribute("AppListEntry") is not null),
            "AppListEntry must not appear anywhere; it is what made the package headless");

        XElement alias = document.Descendants()
            .Single(static element => element.Name.LocalName == "ExecutionAlias");
        Assert.AreEqual(PackageManifestPolicy.ExpectedMcpAlias, (string?)alias.Attribute("Alias"));

        // The alias names an executable other than the one hosting it, which is what lets a single
        // application entry publish the server.
        XElement extension = document.Descendants()
            .Single(static element => (string?)element.Attribute("Category") == "windows.appExecutionAlias");
        Assert.AreEqual(PackageManifestPolicy.ExpectedMcpExecutable, (string?)extension.Attribute("Executable"));
    }

    [TestMethod]
    public void Test_policy_rejects_a_manifest_that_hides_an_application_from_the_app_list()
    {
        XDocument document = LoadManifest();
        document.Descendants()
            .First(static element => element.Name.LocalName == "VisualElements")
            .SetAttributeValue("AppListEntry", "none");

        IReadOnlyList<string> violations = PackageManifestPolicy.Evaluate(document);

        Assert.IsTrue(
            violations.Any(static violation => violation.Contains("HeadlessAppBypass", StringComparison.Ordinal)),
            "Policy accepted the shape Partner Center rejects: " + string.Join(" | ", violations));
    }

    [TestMethod]
    public void Test_policy_rejects_a_manifest_whose_mcp_server_lost_its_alias()
    {
        XDocument document = LoadManifest();
        document.Descendants()
            .Single(static element => element.Name.LocalName == "ExecutionAlias")
            .Remove();

        IReadOnlyList<string> violations = PackageManifestPolicy.Evaluate(document);

        Assert.IsTrue(
            violations.Any(static violation => violation.Contains("appExecutionAlias", StringComparison.Ordinal)),
            "Policy accepted an MCP server that no client could launch: " + string.Join(" | ", violations));
    }
}
