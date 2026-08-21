using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Daynote.App.Settings;

/// <summary>
/// The AI integration row: registering the MCP server that ships in the package with the MCP clients
/// on this machine (docs/MCP.md). Split out of SettingsViewModel.cs to keep that file within the
/// reviewable-size limit.
/// </summary>
public sealed partial class SettingsViewModel
{
    // ── AI integration (MCP) ──

    /// <summary>
    /// Registers the MCP server that ships inside the package. Null in a build composed without it,
    /// which hides the whole registration control rather than offering a button that cannot work.
    /// </summary>
    public Daynote.Core.Mcp.IMcpRegistrationService? Mcp { get; init; }

    /// <summary>
    /// False when no server command is reachable (an unpackaged dev run with nothing built). The row
    /// then explains the situation instead of showing an inert button.
    /// </summary>
    public bool McpAvailable => Mcp?.ServerCommand is not null;

    /// <summary>The one-line Claude Code command, shown read-only and copyable. Language-neutral.</summary>
    public string McpCodeCommand => Mcp?.ClaudeCodeCommand ?? string.Empty;

    /// <summary>Transient "copied" flash shown after the copy button is pressed.</summary>
    [ObservableProperty]
    private bool _mcpCopied;

    /// <summary>Result line under the registration row (success, already-done, or failure + path).</summary>
    [ObservableProperty]
    private string? _mcpStatusText;

    /// <summary>
    /// One-click Claude Desktop registration. The config write is additive, so the only failure the
    /// user has to act on is an unreadable config - and for that the message names the file.
    /// </summary>
    [RelayCommand]
    private async Task RegisterMcpAsync()
    {
        if (Mcp is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            Daynote.Core.Mcp.McpRegistrationResult result =
                await Mcp.RegisterClaudeDesktopAsync().ConfigureAwait(true);
            McpStatusText = result.Outcome switch
            {
                Daynote.Core.Mcp.McpRegistrationOutcome.Registered => Localization.AppStrings.SettingsMcpRegistered,
                Daynote.Core.Mcp.McpRegistrationOutcome.AlreadyRegistered => Localization.AppStrings.SettingsMcpAlreadyRegistered,
                Daynote.Core.Mcp.McpRegistrationOutcome.Unavailable => Localization.AppStrings.SettingsMcpUnavailable,
                _ => string.Format(
                    CultureInfo.CurrentCulture, Localization.AppStrings.SettingsMcpFailedFormat, result.ConfigPath),
            };
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Copies the given code/config text to the clipboard and briefly flashes a confirmation.</summary>
    [RelayCommand]
    private async Task CopyToClipboard(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try { System.Windows.Clipboard.SetText(text); }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or System.Runtime.InteropServices.COMException or System.Threading.ThreadStateException)
        { return; } // clipboard was busy/locked; best-effort
        McpCopied = true;
        try { await Task.Delay(TimeSpan.FromSeconds(1.5)); } catch { }
        McpCopied = false;
    }
}
