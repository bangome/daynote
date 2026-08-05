using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Daynote.Infrastructure.Tests.Packaging;

/// <summary>
/// Static policy evaluator for the Daynote development MSIX manifest (plan Todo 11).
/// It parses <c>Package.appxmanifest</c> and returns a violation for every packaging
/// invariant that is not satisfied. Both the positive test (real manifest → no
/// violations) and the negative test (a mutated copy → a specific violation) run
/// through this one evaluator, so the check is real logic rather than a text match.
/// </summary>
internal static class PackageManifestPolicy
{
    private static readonly XNamespace Foundation = "http://schemas.microsoft.com/appx/manifest/foundation/windows10";
    private static readonly XNamespace Uap = "http://schemas.microsoft.com/appx/manifest/uap/windows10";
    private static readonly XNamespace Rescap = "http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities";
    private static readonly XNamespace Desktop = "http://schemas.microsoft.com/appx/manifest/desktop/windows10";
    private static readonly XNamespace Desktop6 = "http://schemas.microsoft.com/appx/manifest/desktop/windows10/6";

    /// <summary>The StartupTask id must match Daynote.App's ServiceRegistration.StartupTaskId.</summary>
    public const string ExpectedStartupTaskId = "DaynoteStartupTask";

    /// <summary>Minimum OS build, matching the TFM net10.0-windows10.0.19041.0.</summary>
    public const string ExpectedMinVersion = "10.0.19041.0";

    public const string ExpectedIdentityName = "Daynote.Dev";
    public const string ExpectedPublisher = "CN=Daynote.Dev";
    public const string ExpectedArchitecture = "x64";

    public static IReadOnlyList<string> Evaluate(XDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var violations = new List<string>();

        XElement? package = document.Root;
        if (package is null || package.Name != Foundation + "Package")
        {
            violations.Add("Root element is not appx foundation <Package>.");
            return violations;
        }

        // Identity + version well-formed, x64 only.
        XElement? identity = package.Element(Foundation + "Identity");
        if (identity is null)
        {
            violations.Add("Missing <Identity>.");
        }
        else
        {
            if ((string?)identity.Attribute("Name") != ExpectedIdentityName)
            {
                violations.Add($"Identity/@Name must be '{ExpectedIdentityName}'.");
            }

            if ((string?)identity.Attribute("Publisher") != ExpectedPublisher)
            {
                violations.Add($"Identity/@Publisher must be '{ExpectedPublisher}'.");
            }

            string? version = (string?)identity.Attribute("Version");
            if (version is null || !Regex.IsMatch(version, @"^\d+\.\d+\.\d+\.\d+$"))
            {
                violations.Add("Identity/@Version must be a well-formed 4-part version.");
            }

            string? architecture = (string?)identity.Attribute("ProcessorArchitecture");
            if (architecture != ExpectedArchitecture)
            {
                violations.Add($"Identity/@ProcessorArchitecture must be '{ExpectedArchitecture}' (x64 only).");
            }
        }

        // Data durability: file-system write virtualization disabled.
        XElement? properties = package.Element(Foundation + "Properties");
        string? virtualization = properties?.Element(Desktop6 + "FileSystemWriteVirtualization")?.Value;
        if (!string.Equals(virtualization, "disabled", StringComparison.Ordinal))
        {
            violations.Add("Properties/desktop6:FileSystemWriteVirtualization must be 'disabled' so %LocalAppData%\\Daynote is not package-virtualized.");
        }

        // Minimum/target OS build matches the TFM.
        XElement? targetDeviceFamily = package
            .Element(Foundation + "Dependencies")?
            .Element(Foundation + "TargetDeviceFamily");
        if (targetDeviceFamily is null)
        {
            violations.Add("Missing <Dependencies>/<TargetDeviceFamily>.");
        }
        else
        {
            if ((string?)targetDeviceFamily.Attribute("MinVersion") != ExpectedMinVersion)
            {
                violations.Add($"TargetDeviceFamily/@MinVersion must be '{ExpectedMinVersion}' (matches the TFM).");
            }

            string? maxTested = (string?)targetDeviceFamily.Attribute("MaxVersionTested");
            if (maxTested is null || !Regex.IsMatch(maxTested, @"^\d+\.\d+\.\d+\.\d+$"))
            {
                violations.Add("TargetDeviceFamily/@MaxVersionTested must be a well-formed 4-part version.");
            }
        }

        // Capabilities: full trust + unvirtualized resources.
        var capabilities = package
            .Element(Foundation + "Capabilities")?
            .Elements(Rescap + "Capability")
            .Select(static element => (string?)element.Attribute("Name"))
            .ToList() ?? new List<string?>();
        if (!capabilities.Contains("runFullTrust"))
        {
            violations.Add("Missing rescap:Capability 'runFullTrust' (full-trust desktop app).");
        }

        if (!capabilities.Contains("unvirtualizedResources"))
        {
            violations.Add("Missing rescap:Capability 'unvirtualizedResources' (required to disable virtualization).");
        }

        // StartupTask present AND disabled by default with the expected id.
        var startupTasks = package
            .Element(Foundation + "Applications")?
            .Elements(Foundation + "Application")
            .Elements(Foundation + "Extensions")
            .Elements(Desktop + "Extension")
            .Where(static extension => (string?)extension.Attribute("Category") == "windows.startupTask")
            .Elements(Desktop + "StartupTask")
            .ToList() ?? new List<XElement>();
        if (startupTasks.Count == 0)
        {
            violations.Add("Missing windows.startupTask extension.");
        }
        else
        {
            foreach (XElement startupTask in startupTasks)
            {
                if ((string?)startupTask.Attribute("TaskId") != ExpectedStartupTaskId)
                {
                    violations.Add($"StartupTask/@TaskId must be '{ExpectedStartupTaskId}'.");
                }

                if (!string.Equals((string?)startupTask.Attribute("Enabled"), "false", StringComparison.OrdinalIgnoreCase))
                {
                    violations.Add("StartupTask/@Enabled must be 'false' (disabled by default; the app never auto-enables).");
                }
            }
        }

        return violations;
    }
}
