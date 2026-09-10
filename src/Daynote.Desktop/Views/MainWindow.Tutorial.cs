using System.ComponentModel;
using Daynote.App.Onboarding;
using Daynote.Desktop.ViewModels;

namespace Daynote.Desktop.Views;

/// <summary>
/// The tutorial's sticky-note step shows a real sticky note. Same behaviour as the WPF shell
/// (<c>ProductWindow.Tutorial.cs</c>): opened when the step is entered, closed when it is left, and
/// left alone if the user closes it first. Only the sticky opened here is ever closed here.
/// </summary>
public partial class MainWindow
{
    private DesktopShellViewModel? _tutorialShell;
    private TutorialViewModel? _observedTutorial;
    private StickyNoteWindow? _tutorialSticky;

    private void AttachTutorialStickyDemo(DesktopShellViewModel? shell)
    {
        if (_tutorialShell is not null)
        {
            _tutorialShell.PropertyChanged -= OnShellPropertyChangedForTutorial;
        }

        _tutorialShell = shell;
        if (_tutorialShell is not null)
        {
            _tutorialShell.PropertyChanged += OnShellPropertyChangedForTutorial;
        }

        ObserveTutorial(shell?.Tutorial);
    }

    private void OnShellPropertyChangedForTutorial(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DesktopShellViewModel.Tutorial))
        {
            ObserveTutorial(_tutorialShell?.Tutorial);
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

    private void SyncTutorialSticky()
    {
        bool wanted = _observedTutorial is { IsOpen: true } tutorial && tutorial.CurrentStep.ShowsStickyNote;

        if (wanted && _tutorialSticky is null && _shell is not null)
        {
            var sticky = new StickyNoteWindow { DataContext = _shell };
            sticky.Closed += OnTutorialStickyClosed;
            _stickyNotes.Add(sticky);
            _tutorialSticky = sticky;
            sticky.Show();

            // Upper right of the shell, clear of the centred tutorial card and its buttons.
            if (WindowState != Avalonia.Controls.WindowState.Minimized)
            {
                // Window positions are physical pixels on Windows but points on macOS, where the
                // backend never applies the Retina factor to them. Scaling by RenderScaling there
                // doubled the offset and pushed the note past the right edge of the screen.
                double scale = OperatingSystem.IsMacOS() ? 1 : RenderScaling;

                // Bounds is still empty right after Show(); the declared size is the real one then.
                double stickyWidth = sticky.Bounds.Width > 0 ? sticky.Bounds.Width : sticky.Width;
                double stickyHeight = sticky.Bounds.Height > 0 ? sticky.Bounds.Height : sticky.Height;

                int x = Position.X + (int)((Bounds.Width - stickyWidth - 48) * scale);
                int y = Position.Y + (int)(120 * scale);

                // Keep the whole note on the screen the shell is on, whatever the shell's own size.
                if (Screens.ScreenFromWindow(this) is { } screen)
                {
                    Avalonia.PixelRect area = screen.WorkingArea;
                    int w = (int)(stickyWidth * scale);
                    int h = (int)(stickyHeight * scale);
                    x = Math.Clamp(x, area.X, Math.Max(area.X, area.Right - w));
                    y = Math.Clamp(y, area.Y, Math.Max(area.Y, area.Bottom - h));
                }

                sticky.Position = new Avalonia.PixelPoint(x, y);
            }
        }
        else if (!wanted && _tutorialSticky is { } open)
        {
            _tutorialSticky = null;
            open.Closed -= OnTutorialStickyClosed;
            _stickyNotes.Remove(open);
            open.Close();
        }
    }

    private void OnTutorialStickyClosed(object? sender, EventArgs e)
    {
        if (sender is StickyNoteWindow sticky)
        {
            sticky.Closed -= OnTutorialStickyClosed;
            _stickyNotes.Remove(sticky);
        }

        _tutorialSticky = null;
    }
}
