using System.Windows;
using WpfUserControl = System.Windows.Controls.UserControl;

namespace Daynote.App.Notes;

public partial class NoteWorkspaceView : WpfUserControl
{
    public NoteWorkspaceView()
    {
        InitializeComponent();
    }

    private NoteWorkspaceViewModel? ViewModel => DataContext as NoteWorkspaceViewModel;

    private void OnBold(object sender, RoutedEventArgs e) => ApplyFormat(MarkdownCommand.Bold);

    private void OnItalic(object sender, RoutedEventArgs e) => ApplyFormat(MarkdownCommand.Italic);

    private void OnBulletedList(object sender, RoutedEventArgs e) => ApplyFormat(MarkdownCommand.BulletedList);

    private void OnNumberedList(object sender, RoutedEventArgs e) => ApplyFormat(MarkdownCommand.NumberedList);

    private void OnInlineCode(object sender, RoutedEventArgs e) => ApplyFormat(MarkdownCommand.InlineCode);

    private void ApplyFormat(MarkdownCommand command)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        MarkdownEdit edit = viewModel.ApplyFormat(command, Editor.SelectionStart, Editor.SelectionLength);
        Editor.Focus();
        Editor.Select(edit.SelectionStart, edit.SelectionLength);
    }
}
