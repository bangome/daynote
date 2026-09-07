using System.Runtime.Versioning;
using Daynote.Core.Startup;
using Microsoft.Win32;

namespace Daynote.Infrastructure.Startup;

/// <summary>
/// The Windows "start at sign-in" gateway for an unpackaged build: a value under
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>. The MSIX build uses the StartupTask
/// API instead; this is what an .exe outside a package has. Enabled means the value exists.
/// </summary>
/// <remarks>
/// It lives beside <see cref="MsixStartupTaskService"/> rather than in the app, so the two ways
/// Windows can start a program at sign-in sit together and both can be tested without a UI.
/// <para>
/// One thing it cannot do, which the StartupTask API can: notice that the user switched the entry
/// off in Task Manager. The value stays in the registry, so <see cref="GetStateAsync"/> keeps
/// answering <c>Enabled</c>. Copy that says "turned off in Windows startup settings" does not apply
/// to a build using this gateway.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class WindowsRunKeyStartupTaskGateway : IStartupTaskGateway
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private readonly string _valueName;
    private readonly string _command;

    public WindowsRunKeyStartupTaskGateway(string valueName, string executablePath)
    {
        _valueName = string.IsNullOrWhiteSpace(valueName) ? throw new ArgumentException("Value name required.", nameof(valueName)) : valueName;
        _command = string.IsNullOrWhiteSpace(executablePath)
            ? throw new ArgumentException("Executable path required.", nameof(executablePath))
            : $"\"{executablePath}\"";
    }

    public ValueTask<StartupTaskState> GetStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return ValueTask.FromResult(key?.GetValue(_valueName) is string ? StartupTaskState.Enabled : StartupTaskState.Disabled);
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return ValueTask.FromResult(StartupTaskState.Unavailable);
        }
    }

    public ValueTask<StartupTaskState> RequestEnableAsync(CancellationToken cancellationToken)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            key.SetValue(_valueName, _command, RegistryValueKind.String);
            return ValueTask.FromResult(StartupTaskState.Enabled);
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return ValueTask.FromResult(StartupTaskState.Unavailable);
        }
    }

    public ValueTask<StartupTaskState> DisableAsync(CancellationToken cancellationToken)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(_valueName, throwOnMissingValue: false);
            return ValueTask.FromResult(StartupTaskState.Disabled);
        }
        catch (Exception exception) when (exception is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return ValueTask.FromResult(StartupTaskState.Unavailable);
        }
    }
}
