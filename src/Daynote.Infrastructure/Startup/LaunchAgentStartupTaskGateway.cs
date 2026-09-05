using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security;
using System.Text;

namespace Daynote.Infrastructure.Startup;

/// <summary>
/// The macOS "open at login" gateway: a per-user LaunchAgent property list under
/// <c>~/Library/LaunchAgents</c>. Enabled means the plist exists; disabling deletes it. After writing
/// the file the agent is also handed to <c>launchctl</c> so the change is live now and not only at the
/// next login, but a launchctl failure is not an error: the file alone is what login honours.
/// </summary>
/// <remarks>
/// launchd has no notion of "disabled by the user" or "by policy" for a plain LaunchAgent, so those
/// states never come back from here; the service's no-auto-enable policy still applies unchanged.
/// </remarks>
[SupportedOSPlatform("macos")]
public sealed class LaunchAgentStartupTaskGateway : IStartupTaskGateway
{
    private readonly string _plistPath;
    private readonly string _label;
    private readonly string _executablePath;
    private readonly bool _useLaunchctl;

    /// <param name="label">Reverse-DNS agent label, e.g. <c>cc.arachat.daynote</c>.</param>
    /// <param name="executablePath">The binary launchd should start at login.</param>
    /// <param name="launchAgentsDirectory">Defaults to <c>~/Library/LaunchAgents</c>; tests inject a temp folder.</param>
    /// <param name="useLaunchctl">False keeps the gateway file-only, which is what tests want.</param>
    public LaunchAgentStartupTaskGateway(
        string label,
        string executablePath,
        string? launchAgentsDirectory = null,
        bool useLaunchctl = true)
    {
        _label = string.IsNullOrWhiteSpace(label) ? throw new ArgumentException("Label required.", nameof(label)) : label;
        _executablePath = string.IsNullOrWhiteSpace(executablePath)
            ? throw new ArgumentException("Executable path required.", nameof(executablePath))
            : executablePath;
        string directory = launchAgentsDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents");
        _plistPath = Path.Combine(directory, $"{_label}.plist");
        _useLaunchctl = useLaunchctl;
    }

    public string PlistPath => _plistPath;

    public ValueTask<Core.Startup.StartupTaskState> GetStateAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(File.Exists(_plistPath)
            ? Core.Startup.StartupTaskState.Enabled
            : Core.Startup.StartupTaskState.Disabled);

    public async ValueTask<Core.Startup.StartupTaskState> RequestEnableAsync(CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_plistPath)!);
            string temporary = _plistPath + ".tmp";
            await File.WriteAllTextAsync(temporary, BuildPlist(), new UTF8Encoding(false), cancellationToken).ConfigureAwait(false);
            File.Move(temporary, _plistPath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Core.Startup.StartupTaskState.Unavailable;
        }

        if (_useLaunchctl)
        {
            await RunLaunchctlAsync(["bootstrap", $"gui/{GetUid()}", _plistPath], cancellationToken).ConfigureAwait(false);
        }

        return Core.Startup.StartupTaskState.Enabled;
    }

    public async ValueTask<Core.Startup.StartupTaskState> DisableAsync(CancellationToken cancellationToken)
    {
        if (_useLaunchctl)
        {
            await RunLaunchctlAsync(["bootout", $"gui/{GetUid()}/{_label}"], cancellationToken).ConfigureAwait(false);
        }

        try
        {
            if (File.Exists(_plistPath))
            {
                File.Delete(_plistPath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Core.Startup.StartupTaskState.Unavailable;
        }

        return Core.Startup.StartupTaskState.Disabled;
    }

    /// <summary>Testable core: the exact plist text written for this agent.</summary>
    public string BuildPlist() => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
        <plist version="1.0">
        <dict>
            <key>Label</key>
            <string>{SecurityElement.Escape(_label)}</string>
            <key>ProgramArguments</key>
            <array>
                <string>{SecurityElement.Escape(_executablePath)}</string>
            </array>
            <key>RunAtLoad</key>
            <true/>
            <key>ProcessType</key>
            <string>Interactive</string>
            <key>LimitLoadToSessionType</key>
            <string>Aqua</string>
        </dict>
        </plist>

        """;

    private static async Task RunLaunchctlAsync(string[] arguments, CancellationToken cancellationToken)
    {
        try
        {
            var start = new ProcessStartInfo("/bin/launchctl")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (string argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            using Process? process = Process.Start(start);
            if (process is not null)
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is IOException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // Best effort only: the plist on disk is the durable state.
        }
    }

    private static uint GetUid() => getuid();

    [System.Runtime.InteropServices.DllImport("libc")]
    private static extern uint getuid();
}
