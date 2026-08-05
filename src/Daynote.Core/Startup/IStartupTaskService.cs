namespace Daynote.Core.Startup;

/// <summary>
/// The Windows startup-task state as reported by the OS. The default is disabled; the user or an OS
/// policy may pin it to a disabled state that the application must never silently override.
/// </summary>
public enum StartupTaskState
{
    /// <summary>The startup task cannot be queried (unpackaged/dev run). Treated as off.</summary>
    Unavailable = 0,

    /// <summary>Disabled and enabling is permitted.</summary>
    Disabled = 1,

    /// <summary>Enabled and running at logon.</summary>
    Enabled = 2,

    /// <summary>The user disabled it in Windows Settings; enabling from the app is refused.</summary>
    DisabledByUser = 3,

    /// <summary>An administrative policy disabled it; enabling from the app is refused.</summary>
    DisabledByPolicy = 4,

    /// <summary>An administrative policy enabled it; the app cannot change it.</summary>
    EnabledByPolicy = 5,
}

/// <summary>The outcome of an enable request. The service never auto-enables or retries on refusal.</summary>
public readonly record struct StartupEnableResult(StartupTaskState State, bool Changed)
{
    /// <summary>True only when the task is now enabled (by this request or already so).</summary>
    public bool IsEnabled => State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;

    /// <summary>True when the state is fixed by user/policy and cannot be changed from the app.</summary>
    public bool IsUserOrPolicyControlled =>
        State is StartupTaskState.DisabledByUser or StartupTaskState.DisabledByPolicy or StartupTaskState.EnabledByPolicy;
}

/// <summary>
/// Reads and (only when permitted) requests enabling the opt-in Windows startup task. Enabling is
/// never automatic: a user- or policy-disabled state is reported as-is and left unchanged.
/// </summary>
public interface IStartupTaskService
{
    ValueTask<StartupTaskState> GetStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests enabling only when the current state is <see cref="StartupTaskState.Disabled"/>.
    /// User-, policy-, or unavailable states are returned unchanged without any enable attempt.
    /// </summary>
    ValueTask<StartupEnableResult> RequestEnableAsync(CancellationToken cancellationToken = default);

    /// <summary>Disables the task when it is under app control; policy states are left unchanged.</summary>
    ValueTask<StartupEnableResult> RequestDisableAsync(CancellationToken cancellationToken = default);
}
