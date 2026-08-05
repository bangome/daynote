using System.Diagnostics;
using System.IO;

namespace Daynote.UiQa.Product;

/// <summary>
/// Runs a repository QA PowerShell script (clipboard publishing, packaged-app orchestration) and
/// returns its exit code and captured output. Used only inside a live scenario run.
/// </summary>
public static class PowerShellRunner
{
    public readonly record struct ScriptResult(int ExitCode, string StandardOutput, string StandardError);

    public static ScriptResult Run(string scriptPath, params string[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptPath);
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException($"QA script not found: {scriptPath}", scriptPath);
        }

        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Failed to start powershell.exe for '{scriptPath}'.");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(30000);
        return new ScriptResult(process.ExitCode, stdout, stderr);
    }

    /// <summary>Resolves a script under the repository <c>qa/</c> directory relative to the harness.</summary>
    public static string QaScript(string fileName)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "qa", fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate qa/{fileName} above {AppContext.BaseDirectory}.");
    }
}
