using System.Windows;
using System.Windows.Input;
using WpfDataObject = System.Windows.DataObject;
using WpfDragDropEffects = System.Windows.DragDropEffects;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;

namespace Daynote.App.Shell.Product;

/// <summary>
/// One attachment card: drag it into the editor body to drop a <c>[[file:…]]</c> link, double-click it
/// to write a copy to disk.
/// </summary>
/// <remarks>
/// Both gestures were handlers on ProductWindow before the card moved out of it, and neither needed
/// the window — they work from the element and its own view model, so they came along.
/// </remarks>
public partial class FileCardView : System.Windows.Controls.UserControl
{
    private WpfPoint _dragOrigin;

    public FileCardView() => InitializeComponent();

    private FileItemViewModel? Item => DataContext as FileItemViewModel;

    private void OnFileCardMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragOrigin = e.GetPosition(null);

        // Double-click saves a copy. Read from the click count rather than a MouseDoubleClick handler:
        // the card's root is a Border, and that event belongs to Control.
        if (e.ClickCount == 2 && Item is { } file)
        {
            file.SaveCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Starts the drag only once the pointer passes the OS threshold, so a plain click — the Delete
    /// button, or the first half of a double-click — never turns into an accidental drag.
    /// </summary>
    private void OnFileCardMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || Item is not { } file)
        {
            return;
        }

        Vector delta = e.GetPosition(null) - _dragOrigin;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        DragDrop.DoDragDrop(
            this,
            new WpfDataObject(FileLinkSyntax.DragFormat, file.Name),
            WpfDragDropEffects.Copy);
    }
}
