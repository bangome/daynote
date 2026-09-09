using System.ComponentModel;
using Avalonia;
using Avalonia.Threading;
using Daynote.Desktop.ViewModels;

namespace Daynote.Desktop.Views;

/// <summary>
/// Keeps the search results panel under the search box.
/// </summary>
/// <remarks>
/// The panel lives in the root grid — the body's middle column put it under the note title, nowhere
/// near the box, and clipped it — so its horizontal position has to be set rather than declared. It
/// cannot be a margin in markup either: the title bar's left inset is applied per platform at
/// runtime (MainWindow.Chrome.cs), and the box moves with the window's width.
/// </remarks>
public partial class MainWindow
{
    /// <summary>Gap between the search box and the panel below it.</summary>
    private const double SearchDropGap = 4;

    /// <summary>Smallest margin the panel keeps from the window's edges.</summary>
    private const double SearchDropEdge = 8;

    private DesktopShellViewModel? _searchShell;

    private void AttachSearchDropdown(DesktopShellViewModel? shell)
    {
        if (_searchShell?.Search is { } previous)
        {
            previous.PropertyChanged -= OnSearchPropertyChanged;
        }

        _searchShell = shell;
        if (_searchShell?.Search is { } current)
        {
            current.PropertyChanged += OnSearchPropertyChanged;
        }

        PlaceSearchDropdown();
    }

    private void OnSearchPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(_searchShell.Search.IsOpen) or nameof(_searchShell.Search.Results))
        {
            PlaceSearchDropdown();
        }
    }

    /// <summary>
    /// Aligns the panel's left edge with the box's, clamped so a narrow window cannot push it off.
    /// Deferred a dispatcher pass: the box's position is not settled at the instant the query changes.
    /// </summary>
    private void PlaceSearchDropdown() => Dispatcher.UIThread.Post(
        () =>
        {
            if (SearchBox.TranslatePoint(default, this) is not { } origin || Bounds.Width <= 0)
            {
                return;
            }

            double width = SearchDropdown.Width;
            double left = Math.Clamp(origin.X, SearchDropEdge, Math.Max(SearchDropEdge, Bounds.Width - width - SearchDropEdge));
            SearchDropdown.Margin = new Thickness(left, origin.Y + SearchBox.Bounds.Height + SearchDropGap, 0, 0);
        },
        DispatcherPriority.Loaded);
}
