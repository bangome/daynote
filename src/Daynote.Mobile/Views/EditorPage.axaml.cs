using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Daynote.Mobile.ViewModels;

namespace Daynote.Mobile.Views;

/// <summary>
/// The note, full screen: its title, its tags, its text, and the helpers that put the body's small
/// syntax within reach of a thumb.
/// </summary>
public partial class EditorPage : UserControl
{
    public EditorPage()
    {
        InitializeComponent();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    private MobileShellViewModel? _observed;

    /// <summary>
    /// Follows the shell's request to open the title for editing.
    /// </summary>
    /// <remarks>
    /// The old subscription is dropped first: this fires again whenever the page is re-parented or
    /// the shell is replaced, and a second handler would focus the box twice.
    /// </remarks>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_observed is { } previous)
        {
            previous.TitleRenameStarted -= OnTitleRenameStarted;
        }

        _observed = DataContext as MobileShellViewModel;
        if (_observed is { } shell)
        {
            shell.TitleRenameStarted += OnTitleRenameStarted;
        }
    }

    private void OnTitleRenameStarted(object? sender, EventArgs e) => FocusTitleBox();

    /// <summary>
    /// Runs the helper toolbar's fill under the home indicator, with its buttons kept above it; on a
    /// screen with no indicator it keeps 8 points under the buttons, as it does above them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The whole page is extended rather than the toolbar alone: a child drawn past its page's
    /// bottom edge is clipped there, so the strip came out in the page colour.
    /// </para>
    /// <para>
    /// While the keyboard is up the page stops at its top edge instead, <paramref name="keyboard"/>
    /// points above the content's bottom, so the helpers stay in reach of the thumb that is typing.
    /// </para>
    /// </remarks>
    public void SetBottomInset(double bottom, double keyboard = 0)
    {
        if (keyboard > 0)
        {
            Margin = new Thickness(0, 0, 0, keyboard);
            bottom = 0;
        }
        else
        {
            Margin = new Thickness(0, 0, 0, -bottom);
        }

        if (this.FindControl<Border>("Toolbar") is { } toolbar)
        {
            toolbar.Padding = new Thickness(0, 0, 0, bottom > 0 ? bottom : 8);
        }
    }

    /// <summary>
    /// Puts the caret in the title box, with the old name selected so typing replaces it.
    /// </summary>
    /// <remarks>
    /// Posted rather than called straight through: the box becomes visible on the next layout pass,
    /// and focusing a collapsed control does nothing at all.
    /// </remarks>
    private void FocusTitleBox() => Dispatcher.UIThread.Post(() =>
    {
        if (this.FindControl<TextBox>("TitleBox") is { } box)
        {
            box.Focus();
            box.SelectAll();
        }
    });

    private void OnTitleKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MobileShellViewModel shell)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            shell.CommitRenameTitleCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            shell.CancelRenameTitleCommand.Execute(null);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Leaving the box keeps the name. On a phone there is no Escape key in reach, so a tap
    /// anywhere else has to mean something sensible, and "what I typed" is the safer reading of it.
    /// </summary>
    private void OnTitleLostFocus(object? sender, RoutedEventArgs e) =>
        (DataContext as MobileShellViewModel)?.CommitRenameTitleCommand.Execute(null);

    /// <summary>A long press on a file chip: the same menu as the day screen's row.</summary>
    private void OnFileChipContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if ((sender as Control)?.DataContext is MobileFileRowViewModel row)
        {
            row.ShowMenuCommand.Execute(null);
            e.Handled = true;
        }
    }
}
