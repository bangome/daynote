using System.Runtime.Versioning;
using Daynote.Core.Startup;

namespace Daynote.Infrastructure.Startup;

/// <summary>
/// Abstracts the Windows StartupTask API so the service (and its no-auto-enable policy) can be tested
/// without a packaged identity or real registry state.
/// </summary>
public interface IStartupTaskGateway
{
    ValueTask<StartupTaskState> GetStateAsync(CancellationToken cancellationToken);

    /// <summary>Performs the actual OS enable request. Only called after the service has verified the state permits it.</summary>
    ValueTask<StartupTaskState> RequestEnableAsync(CancellationToken cancellationToken);

    /// <summary>Performs the actual OS disable. Only called after the service has verified the state permits it.</summary>
    ValueTask<StartupTaskState> DisableAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The opt-in startup task. It defaults disabled and never auto-enables: enabling is attempted only
/// from a plain <see cref="StartupTaskState.Disabled"/> state, and user-, policy-, or unavailable
/// states are reported unchanged without any enable attempt (plan Todo 10; DESIGN SettingsRow).
/// </summary>
public sealed class MsixStartupTaskService : IStartupTaskService
{
    private readonly IStartupTaskGateway _gateway;

    public MsixStartupTaskService(IStartupTaskGateway gateway)
    {
        _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
    }

    public ValueTask<StartupTaskState> GetStateAsync(CancellationToken cancellationToken = default) =>
        _gateway.GetStateAsync(cancellationToken);

    public async ValueTask<StartupEnableResult> RequestEnableAsync(CancellationToken cancellationToken = default)
    {
        StartupTaskState state = await _gateway.GetStateAsync(cancellationToken).ConfigureAwait(false);
        if (state != StartupTaskState.Disabled)
        {
            // Enabled, unavailable, or user/policy-fixed: report as-is, never retry an enable.
            return new StartupEnableResult(state, Changed: false);
        }

        StartupTaskState next = await _gateway.RequestEnableAsync(cancellationToken).ConfigureAwait(false);
        return new StartupEnableResult(next, Changed: next != state);
    }

    public async ValueTask<StartupEnableResult> RequestDisableAsync(CancellationToken cancellationToken = default)
    {
        StartupTaskState state = await _gateway.GetStateAsync(cancellationToken).ConfigureAwait(false);
        if (state is not (StartupTaskState.Enabled or StartupTaskState.Disabled))
        {
            // Unavailable or policy-fixed: nothing the app may change.
            return new StartupEnableResult(state, Changed: false);
        }

        if (state == StartupTaskState.Disabled)
        {
            return new StartupEnableResult(state, Changed: false);
        }

        StartupTaskState next = await _gateway.DisableAsync(cancellationToken).ConfigureAwait(false);
        return new StartupEnableResult(next, Changed: next != state);
    }
}

#if WINDOWS
/// <summary>
/// Real gateway over <c>Windows.ApplicationModel.StartupTask</c>. When the process has no packaged
/// identity (dev/unpackaged run) every call reports <see cref="StartupTaskState.Unavailable"/> rather
/// than throwing, so the app degrades cleanly.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
public sealed class WindowsStartupTaskGateway : IStartupTaskGateway
{
    private readonly string _taskId;

    public WindowsStartupTaskGateway(string taskId)
    {
        _taskId = string.IsNullOrWhiteSpace(taskId) ? throw new ArgumentException("Task id required.", nameof(taskId)) : taskId;
    }

    public async ValueTask<StartupTaskState> GetStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            Windows.ApplicationModel.StartupTask task =
                await Windows.ApplicationModel.StartupTask.GetAsync(_taskId).AsTask(cancellationToken).ConfigureAwait(false);
            return Map(task.State);
        }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            return StartupTaskState.Unavailable;
        }
    }

    public async ValueTask<StartupTaskState> RequestEnableAsync(CancellationToken cancellationToken)
    {
        try
        {
            Windows.ApplicationModel.StartupTask task =
                await Windows.ApplicationModel.StartupTask.GetAsync(_taskId).AsTask(cancellationToken).ConfigureAwait(false);
            Windows.ApplicationModel.StartupTaskState next =
                await task.RequestEnableAsync().AsTask(cancellationToken).ConfigureAwait(false);
            return Map(next);
        }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            return StartupTaskState.Unavailable;
        }
    }

    public async ValueTask<StartupTaskState> DisableAsync(CancellationToken cancellationToken)
    {
        try
        {
            Windows.ApplicationModel.StartupTask task =
                await Windows.ApplicationModel.StartupTask.GetAsync(_taskId).AsTask(cancellationToken).ConfigureAwait(false);
            task.Disable();
            return Map(task.State);
        }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            return StartupTaskState.Unavailable;
        }
    }

    private static bool IsUnavailable(Exception exception) =>
        exception is InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException or NotSupportedException;

    private static StartupTaskState Map(Windows.ApplicationModel.StartupTaskState state) => state switch
    {
        Windows.ApplicationModel.StartupTaskState.Disabled => StartupTaskState.Disabled,
        Windows.ApplicationModel.StartupTaskState.Enabled => StartupTaskState.Enabled,
        Windows.ApplicationModel.StartupTaskState.DisabledByUser => StartupTaskState.DisabledByUser,
        Windows.ApplicationModel.StartupTaskState.DisabledByPolicy => StartupTaskState.DisabledByPolicy,
        Windows.ApplicationModel.StartupTaskState.EnabledByPolicy => StartupTaskState.EnabledByPolicy,
        _ => StartupTaskState.Unavailable,
    };
}
#endif
