namespace Daynote.App.Shell.Product;

/// <summary>The right-panel tabs (Daynote v3 design): 할 일 / 즐겨찾기 / 태그 / 파일.</summary>
public enum RightTab
{
    Todo,
    Favorites,
    Tags,
    Files,
}

/// <summary>A resolved search-result navigation target: a date, an optional note to select, and an optional tab to reveal.</summary>
public readonly record struct SearchNavigation(Daynote.Core.Domain.LocalDate Date, Guid? NoteId, RightTab? Tab);
