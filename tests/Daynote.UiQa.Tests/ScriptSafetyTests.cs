using System.Text.RegularExpressions;

namespace Daynote.UiQa.Tests;

/// <summary>
/// Static safety checks over the QA PowerShell scripts. They guard the two invariants the plan's
/// Todo 12 "Must NOT" list requires: no arbitrary recursive delete, and evidence/payload hygiene.
/// </summary>
[TestClass]
public sealed class ScriptSafetyTests
{
    [TestMethod]
    public void The_authored_qa_scripts_exist()
    {
        foreach (string expected in new[]
        {
            "PublishClipboardImage.ps1",
            "InspectDaynoteDatabase.ps1",
            "Run-DesktopScenarios.ps1",
        })
        {
            string path = Path.Combine(QaRepositoryPaths.QaDirectory, expected);
            Assert.IsTrue(File.Exists(path), $"Missing QA script: {expected}");
        }
    }

    [TestMethod]
    public void No_script_performs_a_recursive_delete_outside_the_uiqa_namespace()
    {
        foreach (string script in QaRepositoryPaths.QaScripts())
        {
            string text = File.ReadAllText(script);
            bool hasRecursiveDelete = Regex.IsMatch(text, @"Remove-Item[^\n]*-Recurse", RegexOptions.IgnoreCase);
            if (!hasRecursiveDelete)
            {
                continue;
            }

            // Any file that deletes recursively must contain the namespaced guard.
            Assert.IsTrue(
                text.Contains(".uiqa", StringComparison.Ordinal),
                $"{Path.GetFileName(script)} deletes recursively but has no .uiqa namespace guard.");
        }
    }

    [TestMethod]
    public void No_script_recursively_deletes_a_dangerous_root()
    {
        // A recursive Remove-Item must never target these broad locations directly.
        string[] dangerous =
        {
            @"Remove-Item[^\n]*\$env:LOCALAPPDATA[^\n]*-Recurse",
            @"Remove-Item[^\n]*\$env:USERPROFILE[^\n]*-Recurse",
            @"Remove-Item[^\n]*\$HOME[^\n]*-Recurse",
            @"Remove-Item[^\n]*\$daynoteRoot[^\n]*-Recurse",
            @"Remove-Item[^\n]*[A-Za-z]:\\[^\n]*-Recurse",
        };

        foreach (string script in QaRepositoryPaths.QaScripts())
        {
            string text = File.ReadAllText(script);
            foreach (string pattern in dangerous)
            {
                Assert.IsFalse(
                    Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase),
                    $"{Path.GetFileName(script)} matches a dangerous recursive-delete pattern: {pattern}");
            }
        }
    }

    [TestMethod]
    public void Run_DesktopScenarios_guard_refuses_non_namespace_paths()
    {
        string text = File.ReadAllText(Path.Combine(QaRepositoryPaths.QaDirectory, "Run-DesktopScenarios.ps1"));
        StringAssert.Contains(text, "Remove-QaNamespaceOnly");
        StringAssert.Contains(text, ".uiqa");
        StringAssert.Contains(text, "throw");
    }

    [TestMethod]
    public void Run_DesktopScenarios_requires_package_path_and_evidence_dir()
    {
        string text = File.ReadAllText(Path.Combine(QaRepositoryPaths.QaDirectory, "Run-DesktopScenarios.ps1"));
        // Both parameters carry the Mandatory attribute.
        Assert.IsTrue(
            Regex.Matches(text, @"\[Parameter\(Mandatory\s*=\s*\$true\)\]").Count >= 2,
            "Run-DesktopScenarios.ps1 must declare PackagePath and EvidenceDir as mandatory.");
        StringAssert.Contains(text, "$PackagePath");
        StringAssert.Contains(text, "$EvidenceDir");
    }

    [TestMethod]
    public void Clipboard_publish_scripts_do_not_persist_raw_payload()
    {
        // The publish scripts write receipts of byte-length/metadata only; the raw text/pixel buffer
        // must never be piped into a file the harness keeps.
        AssertNoPayloadPersistence("PublishThenHoldClipboard.ps1", "$Text");
        AssertNoPayloadPersistence("PublishClipboardImage.ps1", "$buffer");
    }

    private static void AssertNoPayloadPersistence(string scriptName, string payloadVariable)
    {
        string path = Path.Combine(QaRepositoryPaths.QaDirectory, scriptName);
        if (!File.Exists(path))
        {
            return;
        }

        foreach (string line in File.ReadAllLines(path))
        {
            bool writesFile = line.Contains("Set-Content", StringComparison.OrdinalIgnoreCase)
                || line.Contains("Out-File", StringComparison.OrdinalIgnoreCase)
                || line.Contains("Add-Content", StringComparison.OrdinalIgnoreCase);
            if (writesFile)
            {
                Assert.IsFalse(
                    line.Contains(payloadVariable, StringComparison.Ordinal),
                    $"{scriptName} must not write the raw payload variable {payloadVariable} to a file.");
            }
        }
    }
}
