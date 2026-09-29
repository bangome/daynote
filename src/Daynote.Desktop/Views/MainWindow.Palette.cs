using System.ComponentModel;
using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;
using Daynote.App.Shell.Product;
using Daynote.Desktop.ViewModels;

namespace Daynote.Desktop.Views;

/// <summary>
/// The ⌘K palette: focus lands in the query when it opens, Escape or a click outside closes it, Enter
/// takes the first result. It sits 14% down the window, as the design places it.
/// </summary>
public partial class MainWindow
{
    private DesktopShellViewModel? _paletteShell;

    private void AttachPalette(DesktopShellViewModel? shell)
    {
        if (_paletteShell is not null)
        {
            _paletteShell.PropertyChanged -= OnShellPropertyChangedForPalette;
        }

        _paletteShell = shell;
        if (_paletteShell is not null)
        {
            _paletteShell.PropertyChanged += OnShellPropertyChangedForPalette;
        }
    }

    private void OnShellPropertyChangedForPalette(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DesktopShellViewModel.IsPaletteOpen) && _paletteShell is { IsPaletteOpen: true })
        {
            PlacePalette();
            // The IsVisible binding lands on the next layout pass; focusing now would hit a collapsed box.
            Dispatcher.UIThread.Post(() => SearchBox.Focus(), DispatcherPriority.Loaded);
        }
    }

    private void PlacePalette() =>
        Palette.Margin = new Thickness(24, Math.Round(Bounds.Height * 0.14), 24, 0);

    private void OnPaletteKeyDown(object? sender, KeyEventArgs e)
    {
        if (_paletteShell is not { } shell)
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            shell.ClosePaletteCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (shell.Search.IsOpen)
            {
                if (shell.Search.Results.FirstOrDefault() is SearchResultRowViewModel first)
                {
                    first.ActivateCommand.Execute(null);
                }
            }
            else if (shell.QuickActions.FirstOrDefault() is { } action)
            {
                action.RunCommand.Execute(null);
            }

            e.Handled = true;
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
