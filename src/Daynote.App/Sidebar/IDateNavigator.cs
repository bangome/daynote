using Daynote.Core.Domain;

namespace Daynote.App.Sidebar;

/// <summary>
/// The single navigation source connecting the sidebar, mini calendar, date header, and drawer
/// scope. Selecting a date is autosave-guarded and may be canceled by a save failure.
/// </summary>
public interface IDateNavigator
{
    LocalDate SelectedDate { get; }

    Task<bool> SelectDateAsync(LocalDate date, CancellationToken cancellationToken = default);
}
