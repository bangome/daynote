using System.Xml.Linq;

namespace Daynote.Infrastructure.Tests.Packaging;

/// <summary>
/// Static (no-install) validation of the Daynote development MSIX manifest, plan
/// Todo 11. These tests never spawn a subprocess and never touch a cert store or an
/// installed package; they parse the committed Package.appxmanifest only.
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
        Assert.AreEqual("1.0.0.0", (string?)identity.Attribute("Version"));
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
    public void Test_policy_rejects_a_manifest_without_the_virtualization_exclusion()
    {
        // Plan Todo 11 QA-failure: a disposable in-memory copy WITHOUT the
        // FileSystemWriteVirtualization exclusion must be REJECTED by the policy.
        XDocument mutated = LoadManifest();
        XElement virtualization = mutated.Descendants()
            .Single(static element => element.Name.LocalName == "FileSystemWriteVirtualization");
        virtualization.Remove();

        IReadOnlyList<string> violations = PackageManifestPolicy.Evaluate(mutated);

        Assert.IsTrue(
            violations.Any(static v => v.Contains("FileSystemWriteVirtualization", StringComparison.Ordinal)),
            "Removing the virtualization exclusion must produce a naming violation. Got: "
                + string.Join(" | ", violations));
    }

    [TestMethod]
    public void Test_policy_rejects_a_manifest_without_the_unvirtualized_capability()
    {
        // Additional negative: dropping the enabling capability must also be rejected,
        // using a disposable temp-file copy that is cleaned up afterward.
        string tempManifest = Path.Combine(
            Path.GetTempPath(),
            "daynote-task11-manifest",
            Guid.NewGuid().ToString("N"),
            "Package.appxmanifest");
        Directory.CreateDirectory(Path.GetDirectoryName(tempManifest)!);
        try
        {
            XDocument mutated = LoadManifest();
            XElement capability = mutated.Descendants()
                .Single(static element =>
                    element.Name.LocalName == "Capability"
                    && (string?)element.Attribute("Name") == "unvirtualizedResources");
            capability.Remove();
            mutated.Save(tempManifest);

            IReadOnlyList<string> violations = PackageManifestPolicy.Evaluate(XDocument.Load(tempManifest));

            Assert.IsTrue(
                violations.Any(static v => v.Contains("unvirtualizedResources", StringComparison.Ordinal)),
                "Removing the unvirtualizedResources capability must produce a naming violation. Got: "
                    + string.Join(" | ", violations));
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
}
