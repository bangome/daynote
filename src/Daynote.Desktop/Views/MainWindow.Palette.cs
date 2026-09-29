using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Threading;
using Daynote.App.Shell.Product;
using Daynote.Desktop.ViewModels;

namespace Daynote.Desktop.Views;

/// <summary>
/// The ⌘K palette: focus lands in the query when it opens, Up and Down move a highlighted row
/// through the quick actions (or, once something is typed, the results), Enter runs it, Escape or a
/// click outside closes the palette. It sits 14% down the window, as the design places it.
/// </summary>
/// <remarks>
/// The keyboard cursor is the view's, not the view models': the rows are the shared search rows,
/// which carry no selection, and which row is lit only matters to this window. It is drawn with the
/// same class as the pointer's hover.
/// </remarks>
public partial class MainWindow
{
    private DesktopShellViewModel? _paletteShell;
    private int _paletteIndex;

    /// <summary>What had the keyboard before the palette took it, so closing hands it back.</summary>
    private IInputElement? _focusBeforePalette;

    private void AttachPalette(DesktopShellViewModel? shell)
    {
        if (_paletteShell is not null)
        {
            _paletteShell.PropertyChanged -= OnShellPropertyChangedForPalette;
            _paletteShell.Search.PropertyChanged -= OnSearchPropertyChangedForPalette;
            _paletteShell.Search.Results.CollectionChanged -= OnPaletteResultsChanged;
        }

        _paletteShell = shell;
        if (_paletteShell is not null)
        {
            _paletteShell.PropertyChanged += OnShellPropertyChangedForPalette;
            _paletteShell.Search.PropertyChanged += OnSearchPropertyChangedForPalette;
            _paletteShell.Search.Results.CollectionChanged += OnPaletteResultsChanged;
        }
    }

    private void OnShellPropertyChangedForPalette(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DesktopShellViewModel.IsPaletteOpen) || _paletteShell is not { } shell)
        {
            return;
        }

        if (!shell.IsPaletteOpen)
        {
            RestoreFocusAfterPalette(shell);
            return;
        }

        _focusBeforePalette = FocusManager?.GetFocusedElement();
        PlacePalette();
        ResetPaletteCursor();
        // The IsVisible binding lands on the next layout pass; focusing now would hit a collapsed box.
        Dispatcher.UIThread.Post(() => SearchBox.Focus(), DispatcherPriority.Loaded);
    }

    /// <summary>Typing switches between the quick actions and the results: the cursor starts over.</summary>
    private void OnSearchPropertyChangedForPalette(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SearchDropdownViewModel.IsOpen))
        {
            ResetPaletteCursor();
        }
    }

    private void OnPaletteResultsChanged(object? sender, NotifyCollectionChangedEventArgs e) => ResetPaletteCursor();

    /// <summary>
    /// The hidden query box must not keep the keyboard: typing would go on searching out of sight.
    /// Focus goes back to where it was, or to the note body when that is gone (an action may have
    /// opened another view); after the new layout, since what was picked may have changed the view.
    /// </summary>
    private void RestoreFocusAfterPalette(DesktopShellViewModel shell)
    {
        IInputElement? previous = _focusBeforePalette;
        _focusBeforePalette = null;
        Dispatcher.UIThread.Post(
            () =>
            {
                if (previous is Control { IsEffectivelyVisible: true, IsEffectivelyEnabled: true } control
                    && !ReferenceEquals(control, SearchBox) && control.Focus())
                {
                    return;
                }

                if (shell.IsEditorMode && Editor.IsEffectivelyVisible && Editor.Focus())
                {
                    return;
                }

                // Nothing to give it to: at least take it off the hidden box.
                Focus();
            },
            DispatcherPriority.Loaded);
    }

    private void PlacePalette() =>
        Palette.Margin = new Thickness(24, Math.Round(Bounds.Height * 0.14), 24, 0);

    private void ResetPaletteCursor()
    {
        _paletteIndex = 0;
        // Rows arrive as a run of adds and their containers are made on the next layout pass.
        Dispatcher.UIThread.Post(UpdatePaletteHighlight, DispatcherPriority.Loaded);
    }

    /// <summary>The list the cursor moves through now: results once there is a query, else the actions.</summary>
    private ItemsControl ActivePaletteList =>
        _paletteShell is { Search.IsOpen: true } ? ResultList : QuickActionList;

    private static Button? RowAt(ItemsControl list, int index) =>
        list.ContainerFromIndex(index) switch
        {
            Button button => button,
            ContentPresenter { Child: Button button } => button,
            _ => null,
        };

    private void UpdatePaletteHighlight()
    {
        foreach (ItemsControl list in new[] { QuickActionList, ResultList })
        {
            bool active = ReferenceEquals(list, ActivePaletteList);
            for (int i = 0; i < list.ItemCount; i++)
            {
                if (RowAt(list, i) is { } row)
                {
                    row.Classes.Set("highlighted", active && i == _paletteIndex);
                    if (active && i == _paletteIndex)
                    {
                        row.BringIntoView();
                    }
                }
            }
        }
    }

    private void OnPaletteKeyDown(object? sender, KeyEventArgs e)
    {
        if (_paletteShell is not { } shell)
        {
            return;
        }

        int count = ActivePaletteList.ItemCount;
        switch (e.Key)
        {
            case Key.Escape:
                shell.ClosePaletteCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Down or Key.Up when count > 0:
                // Wraps at either end, so the last row is one Up away from the first.
                _paletteIndex = (_paletteIndex + (e.Key == Key.Down ? 1 : -1) + count) % count;
                UpdatePaletteHighlight();
                e.Handled = true;
                break;
            case Key.Enter:
                RunHighlightedPaletteRow(shell);
                e.Handled = true;
                break;
        }
    }

    private void RunHighlightedPaletteRow(DesktopShellViewModel shell)
    {
        if (shell.Search.IsOpen)
        {
            // The results on screen can still be the previous query's while the debounce runs; a
            // row only answers the query it was found for, so Enter waits for the right rows.
            if (_paletteIndex < shell.Search.Results.Count
                && shell.Search.Results[_paletteIndex] is { } row
                && string.Equals(row.Query, shell.Search.Query, StringComparison.Ordinal))
            {
                row.ActivateCommand.Execute(null);
            }
        }
        else if (_paletteIndex < shell.QuickActions.Count)
        {
            shell.QuickActions[_paletteIndex].RunCommand.Execute(null);
        }
    }

    /// <summary>A press on the dimmed ground, not on the palette card, dismisses it.</summary>
    private void OnPaletteScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ReferenceEquals(e.Source, sender))
        {
            _paletteShell?.ClosePaletteCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>The same for the settings dialog.</summary>
    private void OnSettingsScrimPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ReferenceEquals(e.Source, sender) && _shell is { } shell)
        {
            shell.CloseSettingsCommand.Execute(null);
            e.Handled = true;
        }
    }
}
