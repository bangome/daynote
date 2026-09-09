using System.ComponentModel;
using Daynote.App.Onboarding;

namespace Daynote.App.Shell.Product;

/// <summary>
/// The tutorial's sticky-note step shows a real sticky note rather than pointing at the button that
/// opens one.
/// </summary>
/// <remarks>
/// The overlay can only spotlight elements inside this window, and a sticky is a window of its own —
/// so for that one step the demonstration is the thing itself: open a sticky over the dimmed shell,
/// let the card explain it, and close it again the moment the step is left, whether by Next, Back,
/// Skip or Escape. A sticky the user opened themselves is never touched; only the one opened here is.
/// </remarks>
public partial class ProductWindow
{
    private TutorialViewModel? _observedTutorial;
    private StickyNoteWindow? _tutorialSticky;

    private void AttachTutorialStickyDemo()
    {
        ViewModel.PropertyChanged += OnShellPropertyChangedForTutorial;
        ObserveTutorial(ViewModel.Tutorial);
    }

    private void OnShellPropertyChangedForTutorial(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProductShellViewModel.Tutorial))
        {
            ObserveTutorial(ViewModel.Tutorial);
        }
    }

    private void ObserveTutorial(TutorialViewModel? tutorial)
    {
        if (_observedTutorial is not null)
        {
            _observedTutorial.PropertyChanged -= OnTutorialPropertyChanged;
        }

        _observedTutorial = tutorial;
        if (_observedTutorial is not null)
        {
            _observedTutorial.PropertyChanged += OnTutorialPropertyChanged;
        }

        SyncTutorialSticky();
    }

    private void OnTutorialPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TutorialViewModel.IsOpen) or nameof(TutorialViewModel.Index)
            or nameof(TutorialViewModel.CurrentStep))
        {
            SyncTutorialSticky();
        }
    }

    /// <summary>Open the demo sticky exactly while the sticky step is showing; close it otherwise.</summary>
    private void SyncTutorialSticky()
    {
        bool wanted = _observedTutorial is { IsOpen: true } tutorial && tutorial.CurrentStep.ShowsStickyNote;

        if (wanted && _tutorialSticky is null)
        {
            _tutorialSticky = OpenStickyNote();
            _tutorialSticky.Closed += OnTutorialStickyClosed;
            PlaceDemoSticky(_tutorialSticky);
        }
        else if (!wanted && _tutorialSticky is { } sticky)
        {
            _tutorialSticky = null;
            sticky.Closed -= OnTutorialStickyClosed;
            sticky.Close();
        }
    }

    /// <summary>
    /// Over the shell's upper right, clear of the callout. A sticky opens centred on the screen by
    /// default, which on this step put it on top of the card explaining it — and over its Next button.
    /// </summary>
    private void PlaceDemoSticky(StickyNoteWindow sticky)
    {
        if (WindowState == System.Windows.WindowState.Minimized)
        {
            return;
        }

        double width = sticky.ActualWidth > 0 ? sticky.ActualWidth : sticky.Width;
        sticky.Left = Left + ActualWidth - width - 48;
        sticky.Top = Top + 120;
    }

    /// <summary>The user closed the demo sticky themselves; do not reopen it for the same step.</summary>
    private void OnTutorialStickyClosed(object? sender, EventArgs e)
    {
        if (sender is StickyNoteWindow sticky)
        {
            sticky.Closed -= OnTutorialStickyClosed;
        }

        _tutorialSticky = null;
    }
}
