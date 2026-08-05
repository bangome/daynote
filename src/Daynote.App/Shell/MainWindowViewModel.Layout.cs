using CommunityToolkit.Mvvm.Input;
using Daynote.Core.Notes;

namespace Daynote.App.Shell;

public enum CompactWorkspaceView
{
    Navigate,
    Notes,
}

public sealed partial class MainWindowViewModel
{
    public bool IsCompact => LayoutState == AppLayoutState.Compact;

    public bool IsRegular => LayoutState == AppLayoutState.Regular;

    public bool IsWide => LayoutState == AppLayoutState.Wide;

    public bool IsSidebarLayout => LayoutState != AppLayoutState.Compact;

    /// <summary>Applies an effective content width and returns the resolved layout state (with hysteresis).</summary>
    public AppLayoutState UpdateEffectiveWidth(double effectiveWidth)
    {
        LayoutState = _layout.Update(effectiveWidth);
        return LayoutState;
    }

    partial void OnLayoutStateChanged(AppLayoutState value)
    {
        OnPropertyChanged(nameof(IsCompact));
        OnPropertyChanged(nameof(IsRegular));
        OnPropertyChanged(nameof(IsWide));
        OnPropertyChanged(nameof(IsSidebarLayout));
    }

    /// <summary>Reveals the notes region for a deep link (Compact switches to the Notes view).</summary>
    public void RevealNotes()
    {
        if (IsCompact)
        {
            SelectedCompactView = CompactWorkspaceView.Notes;
        }
    }

    /// <summary>Switches the Compact workspace view; leaving Notes first completes a safe autosave flush.</summary>
    [RelayCommand]
    public async Task<bool> SelectCompactViewAsync(CompactWorkspaceView view)
    {
        if (view == SelectedCompactView)
        {
            return true;
        }

        if (SelectedCompactView == CompactWorkspaceView.Notes)
        {
            FlushResult flush = await Notes.FlushAsync(FlushReason.NoteChange).ConfigureAwait(true);
            if (!flush.CanProceed)
            {
                return false;
            }
        }

        SelectedCompactView = view;
        return true;
    }
}
