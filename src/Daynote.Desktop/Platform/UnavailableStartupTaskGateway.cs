using Daynote.Core.Startup;
using Daynote.Infrastructure.Startup;

namespace Daynote.Desktop.Platform;

/// <summary>For operating systems without a login-item integration yet: reports Unavailable, never throws.</summary>
public sealed class UnavailableStartupTaskGateway : IStartupTaskGateway
{
    public ValueTask<StartupTaskState> GetStateAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(StartupTaskState.Unavailable);

    public ValueTask<StartupTaskState> RequestEnableAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(StartupTaskState.Unavailable);

    public ValueTask<StartupTaskState> DisableAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(StartupTaskState.Unavailable);
}
