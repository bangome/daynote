using Daynote.Core.Domain;
using Daynote.Core.Domain.Notes;

namespace Daynote.Core.Notes;

public sealed class GetDayWorkspace(INoteRepository repository)
{
    public ValueTask<DayWorkspace> ExecuteAsync(LocalDate localDate, CancellationToken cancellationToken = default) =>
        repository.GetDayWorkspaceStateAsync(localDate, cancellationToken);
}
