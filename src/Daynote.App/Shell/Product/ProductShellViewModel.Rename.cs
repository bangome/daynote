using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Daynote.App.Shell.Product;

/// <summary>
/// Renaming the open note from its heading. Split from the main partial to keep either file
/// reviewable; the Avalonia shell says the same thing in the same words.
/// </summary>
public sealed partial class ProductShellViewModel
{
    // ── Title rename ─────────────────────────────────────────────────────────────────────────────
    // The title is a label until the user asks to change it: double-click on the heading, or "이름
    // 변경" on a row. Then it is a text box holding a draft, committed on Enter or focus loss and
    // dropped on Escape. The draft is separate from the tab's title so Escape really does cancel.
    // The Avalonia shell does the same, in the same words.

    [ObservableProperty]
    private bool _isRenamingTitle;

    [ObservableProperty]
    private string _titleDraft = string.Empty;

    public void BeginRenameTitle()
    {
        if (Notes.SelectedTab is { } tab)
        {
            TitleDraft = tab.Title;
            IsRenamingTitle = true;
        }
    }

    [RelayCommand]
    private async Task CommitRenameTitle()
    {
        if (!IsRenamingTitle)
        {
            return;
        }

        IsRenamingTitle = false;
        string draft = TitleDraft.Trim();
        if (Notes.SelectedTab is { } tab && draft.Length > 0 && !string.Equals(draft, tab.Title, StringComparison.Ordinal))
        {
            await Notes.RenameAsync(tab, draft).ConfigureAwait(true);
            RefreshHeader();
        }
    }

    [RelayCommand]
    private void CancelRenameTitle() => IsRenamingTitle = false;
}
