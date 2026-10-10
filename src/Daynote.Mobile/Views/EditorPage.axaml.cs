using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Daynote.Mobile.ViewModels;
using Daynote.Motion;

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

        // A paste is not a typed @, however it starts; see OnBodyPropertyChanged.
        BodyBox?.AddHandler(TextBox.PastingFromClipboardEvent, (_, _) => _pasting = true, RoutingStrategies.Bubble);
        BodyBox?.AddHandler(InputElement.TextInputEvent, (_, _) => _pasting = false, RoutingStrategies.Tunnel);
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
            previous.PropertyChanged -= OnShellPropertyChanged;
        }

        _observed = DataContext as MobileShellViewModel;
        if (_observed is { } shell)
        {
            shell.TitleRenameStarted += OnTitleRenameStarted;
            shell.PropertyChanged += OnShellPropertyChanged;
        }
    }

    // ── Motion (spec M2) ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// What was just made opens its row in place; when it settles back into the count, the count
    /// swells once (M2 on the phone's editor, where the day is out of sight).
    /// </summary>
    private void OnShellPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MobileShellViewModel.JustMade) || _observed is not { } shell)
        {
            return;
        }

        if (shell.JustMade is not null && this.FindControl<Grid>("JustMadeRow") is { } row)
        {
            row.Opacity = 0;
            Dispatcher.UIThread.Post(() =>
                _ = MotionPlayer.Play(row, "m2", Choreography.NoticeOpen(row, row.Bounds.Height)), DispatcherPriority.Loaded);
        }
        else if (shell.JustMade is null && this.FindControl<Button>("NoteItemsButton") is { } count)
        {
            Dispatcher.UIThread.Post(() =>
                _ = MotionPlayer.Play(count, "m2", Choreography.CountBump(count)), DispatcherPriority.Loaded);
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

    // ── @ at the start of a line ─────────────────────────────────────────────────

    /// <summary>
    /// An @ typed as the first thing on a line opens the to-do sheet, as the toolbar's + does, and
    /// is taken back out of the note: it was a way in, not part of the text. Anywhere else in a
    /// line an @ is just a character — "jiwon@aegisep.com", "@지원 님께".
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read off the text rather than a key event, because a phone's keyboard does not always send
    /// one: what is checked is that the box gained exactly one character, an @, with nothing but
    /// spaces between it and the line's start. A Korean syllable being composed is not in the text
    /// until it is committed, and a commit that carries it along with an @ is two characters, so
    /// composition never trips it.
    /// </para>
    /// <para>
    /// A paste never does either, even of a lone @: the box says it is about to paste, and the
    /// change that follows is skipped. Keys typed after a paste that came to nothing clear that.
    /// </para>
    /// </remarks>
    private void OnBodyPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != TextBox.TextProperty)
        {
            return;
        }

        if (_pasting)
        {
            _pasting = false;
            return;
        }

        if (DataContext is not MobileShellViewModel { IsEditorOpen: true } shell || BodyBox is not { IsFocused: true } box ||
            TypedAtLineStart(e.GetOldValue<string?>() ?? string.Empty, e.GetNewValue<string?>() ?? string.Empty) is not { } at)
        {
            return;
        }

        // After the box has finished with the keystroke: changing its text from inside its own
        // text change would be undone by the caret move that follows it.
        Dispatcher.UIThread.Post(() =>
        {
            (string text, _) = Read();
            if (at < text.Length && text[at] == '@')
            {
                Write(text.Remove(at, 1), at);
            }

            shell.OpenTodoSheetCommand.Execute(null);
        });
    }

    /// <summary>True between the box announcing a paste and the text change it makes.</summary>
    private bool _pasting;

    /// <summary>
    /// Where the @ went when <paramref name="after"/> is <paramref name="before"/> with one @
    /// added at the start of a line (spaces and tabs before it allowed), or null.
    /// </summary>
    internal static int? TypedAtLineStart(string before, string after)
    {
        if (after.Length != before.Length + 1)
        {
            return null;
        }

        int at = 0;
        while (at < before.Length && before[at] == after[at])
        {
            at += 1;
        }

        if (after[at] != '@' || !after.AsSpan(at + 1).SequenceEqual(before.AsSpan(at)))
        {
            return null;
        }

        int start = at;
        while (start > 0 && after[start - 1] is ' ' or '\t')
        {
            start -= 1;
        }

        return start == 0 || after[start - 1] == '\n' ? at : null;
    }

    /// <summary>The note body, looked up by name rather than held in a field.</summary>
    private TextBox? BodyBox => this.FindControl<TextBox>("Body");

    private (string Text, int Caret) Read()
    {
        if (BodyBox is not { } box)
        {
            return (string.Empty, 0);
        }

        string text = box.Text ?? string.Empty;
        return (text, Math.Clamp(box.CaretIndex, 0, text.Length));
    }

    /// <summary>
    /// Writes the body back through the binding and puts the caret where the edit left it.
    /// </summary>
    /// <remarks>
    /// Setting Text on the box is what raises the binding, which is what reaches autosave; assigning
    /// CaretIndex first would be undone by the text change, so it follows.
    /// </remarks>
    private void Write(string text, int caret)
    {
        if (BodyBox is not { } box)
        {
            return;
        }

        box.Text = text;
        box.CaretIndex = Math.Clamp(caret, 0, text.Length);
    }
}
