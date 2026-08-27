using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Daynote.Infrastructure.Tests.Packaging;

/// <summary>
/// Static policy evaluator for the Daynote Microsoft Store MSIX manifest.
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
    private static readonly XNamespace Uap3 = "http://schemas.microsoft.com/appx/manifest/uap/windows10/3";

    /// <summary>The StartupTask id must match Daynote.App's ServiceRegistration.StartupTaskId.</summary>
    public const string ExpectedStartupTaskId = "DaynoteStartupTask";

    /// <summary>Minimum OS build, matching the TFM net10.0-windows10.0.19041.0.</summary>
    public const string ExpectedMinVersion = "10.0.19041.0";

    /// <summary>The Application id of the bundled MCP stdio server (docs/MCP.md).</summary>
    public const string ExpectedMcpApplicationId = "DaynoteMcp";

    /// <summary>The alias MCP clients are configured with; must match McpServerCommand.PackagedAlias.</summary>
    public const string ExpectedMcpAlias = "daynote-mcp.exe";

    /// <summary>The MCP server sits in the app's folder so both share one copy of the .NET runtime.</summary>
    public const string ExpectedMcpExecutable = @"Daynote.App\Daynote.Mcp.exe";

    /// <summary>
    /// The identity Partner Center reserved for this app. It is not cosmetic: the Store rejects a
    /// package whose identity does not match the reservation, and the old self-signed sideload identity
    /// (Daynote.Dev) would install locally while being unsubmittable. See docs/STORE.md.
    /// </summary>
    public const string ExpectedIdentityName = "BreadJinhwaJeong.-Daynote";

    public const string ExpectedPublisher = "CN=7FDB7ABF-3343-4BA9-9F0C-C601ABED42EE";
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

            // The Store reserves the revision (4th) part and refuses a package that sets it.
            string? version = (string?)identity.Attribute("Version");
            if (version is null || !Regex.IsMatch(version, @"^\d+\.\d+\.\d+\.0$"))
            {
                violations.Add("Identity/@Version must be a 4-part version whose revision part is 0 (Store requirement).");
            }

            string? architecture = (string?)identity.Attribute("ProcessorArchitecture");
            if (architecture != ExpectedArchitecture)
            {
                violations.Add($"Identity/@ProcessorArchitecture must be '{ExpectedArchitecture}' (x64 only).");
            }
        }

        // Standard packaged storage: virtualization stays ENABLED. Disabling it needs the
        // unvirtualizedResources restricted capability, which requires special Microsoft approval, so a
        // package declaring either is not submittable - and the earlier sideload builds did both. The
        // cost of the standard model is that uninstall removes the data, which the in-app
        // Backup/Restore covers (docs/PACKAGING.md, docs/DATA_AND_RECOVERY.md).
        XElement? properties = package.Element(Foundation + "Properties");
        if (properties?.Element(Desktop6 + "FileSystemWriteVirtualization") is not null)
        {
            violations.Add("Properties must not declare desktop6:FileSystemWriteVirtualization; Store packages keep virtualization enabled.");
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

        // Capabilities: full trust only.
        var capabilities = package
            .Element(Foundation + "Capabilities")?
            .Elements(Rescap + "Capability")
            .Select(static element => (string?)element.Attribute("Name"))
            .ToList() ?? new List<string?>();
        if (!capabilities.Contains("runFullTrust"))
        {
            violations.Add("Missing rescap:Capability 'runFullTrust' (full-trust desktop app).");
        }

        if (capabilities.Contains("unvirtualizedResources"))
        {
            violations.Add("rescap:Capability 'unvirtualizedResources' needs special Microsoft approval and must not be declared.");
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

        EvaluateMcpServer(package, violations);

        return violations;
    }

    /// <summary>
    /// The MCP server must ship as a second application in THIS package and be reachable through an
    /// app execution alias. Both halves are load-bearing: packaging it here is what makes the server
    /// see the same virtualized database as the app, and the alias is the only path a client process
    /// can actually launch (the install folder under WindowsApps is ACL-locked). It must also stay out
    /// of the app list, since no user launches a stdio server by hand.
    /// </summary>
    private static void EvaluateMcpServer(XElement package, List<string> violations)
    {
        List<XElement> applications = package
            .Element(Foundation + "Applications")?
            .Elements(Foundation + "Application")
            .ToList() ?? [];

        XElement? mcp = applications.Find(
            application => (string?)application.Attribute("Id") == ExpectedMcpApplicationId);
        if (mcp is null)
        {
            violations.Add($"Missing the MCP server <Application Id='{ExpectedMcpApplicationId}'> (docs/MCP.md).");
            return;
        }

        if ((string?)mcp.Attribute("EntryPoint") != "Windows.FullTrustApplication")
        {
            violations.Add("The MCP server application must be a Windows.FullTrustApplication.");
        }

        // Co-located with the app on purpose: the server shares the app's single copy of the .NET
        // runtime, which is worth ~64 MB of download. Its own folder would be a silent size doubling.
        string? executable = (string?)mcp.Attribute("Executable");
        if (executable != ExpectedMcpExecutable)
        {
            violations.Add($"The MCP server application must point at '{ExpectedMcpExecutable}' (co-located with the app).");
        }

        string? appListEntry = (string?)mcp.Element(Uap + "VisualElements")?.Attribute("AppListEntry");
        if (!string.Equals(appListEntry, "none", StringComparison.Ordinal))
        {
            violations.Add("The MCP server must set uap:VisualElements/@AppListEntry='none' (not user-launchable).");
        }

        List<string?> aliases = mcp
            .Elements(Foundation + "Extensions")
            .Elements(Uap3 + "Extension")
            .Where(static extension => (string?)extension.Attribute("Category") == "windows.appExecutionAlias")
            .Elements(Uap3 + "AppExecutionAlias")
            .Elements(Desktop + "ExecutionAlias")
            .Select(static alias => (string?)alias.Attribute("Alias"))
            .ToList();
        if (!aliases.Contains(ExpectedMcpAlias))
        {
            violations.Add($"Missing the windows.appExecutionAlias '{ExpectedMcpAlias}' on the MCP server application.");
        }
    }
}
