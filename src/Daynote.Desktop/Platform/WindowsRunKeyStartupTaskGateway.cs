using System.Runtime.Versioning;
using Daynote.Core.Startup;
using Daynote.Infrastructure.Startup;
using Microsoft.Win32;

namespace Daynote.Desktop.Platform;

/// <summary>
/// The Windows "start at sign-in" gateway for the unpackaged Avalonia build: a value under
/// <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c>. The MSIX WPF build uses the StartupTask
/// API instead; this is what an .exe outside a package has. Enabled means the value exists and points
/// at this executable.
/// </summary>
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
