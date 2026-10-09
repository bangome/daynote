using System.Globalization;
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

        // On the way down, not on the way up: the box handles Enter itself (that is what puts a
        // line break in the body), and a handler attached after it would never be reached.
        BodyBox?.AddHandler(InputElement.KeyDownEvent, OnBodyKeyDown, RoutingStrategies.Tunnel);
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

        // The page has just been shortened to sit above the keyboard, which is the measurement the
        // bar folds on.
        UpdateCaptureBarRoom();
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

    // ── The @ command (phone §01) ────────────────────────────────────────────────

    /// <summary>
    /// A caret move can open or close the bar just as a keystroke can — tapping away from a
    /// half-typed "@내일" has to dismiss it — so both are the same question, asked here because the
    /// box is the only thing that knows where the caret ended up.
    /// </summary>
    private void OnBodyPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != TextBox.CaretIndexProperty && e.Property != TextBox.TextProperty)
        {
            return;
        }

        if (DataContext is MobileShellViewModel shell && BodyBox is { } box)
        {
            shell.Notes.UpdateCapture(Math.Clamp(box.CaretIndex, 0, (box.Text ?? string.Empty).Length));
            UpdateCaptureBarRoom();
        }
    }

    /// <summary>
    /// The return key, while the bar is up and has read something: it makes the item instead of a
    /// line break. The one key on a phone keyboard that can be given a second job, and only for as
    /// long as the bar is there to say so.
    /// </summary>
    private void OnBodyKeyDown(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        if (args is not KeyEventArgs { Key: Key.Enter } e
            || DataContext is not MobileShellViewModel { Capture: { IsOpen: true, IsPrompting: false } } shell)
        {
            return;
        }

        e.Handled = true;
        shell.CommitCaptureCommand.Execute(null);
    }

    /// <summary>
    /// The @ button: types the character, with a space in front of it when the line needs one.
    /// </summary>
    /// <remarks>
    /// @ only opens the bar at the start of a word, which is what keeps an email address out of
    /// it; a button that pasted one mid-word would do nothing and look broken.
    /// </remarks>
    private void OnInsertAt(object? sender, RoutedEventArgs e)
    {
        (string text, int caret) = Read();
        string insert = caret > 0 && text[caret - 1] is not (' ' or '\n') ? " @" : "@";
        Write(text.Insert(caret, insert), caret + insert.Length);
    }

    /// <summary>A tap on an example types it, so the parser reads it like anything else.</summary>
    private void OnCaptureExample(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not string example)
        {
            return;
        }

        (string text, int caret) = Read();
        Write(text.Insert(caret, example), caret + example.Length);
    }

    /// <summary>
    /// Whether the bar still has room for both readings (§01 ⑤). Measured rather than guessed from
    /// the screen size: a split-screen window and a tall keyboard leave the same gap as a small
    /// phone, and the bar should fold for all three.
    /// </summary>
    private void UpdateCaptureBarRoom()
    {
        if (DataContext is MobileShellViewModel shell)
        {
            shell.IsCaptureBarCompact = Bounds.Height > 0 && Bounds.Height < CaptureBarTwoLineRoom;
        }
    }

    /// <summary>The design's threshold: under this much above the keyboard, one line.</summary>
    private const double CaptureBarTwoLineRoom = 230;

    // ── The writing helpers ──────────────────────────────────────────────────────────────────────

    /// <summary>Stamps the caret's line with the note's own day, as <c>(M/D)</c>.</summary>
    private void OnInsertDate(object? sender, RoutedEventArgs e) => AppendDue(withTime: false);

    /// <summary>The same with a time, <c>(M/D H:mm)</c>, rounded to the next five minutes.</summary>
    private void OnInsertTime(object? sender, RoutedEventArgs e) => AppendDue(withTime: true);

    /// <remarks>
    /// The date is the note's own, not today's: someone writing up Monday on Monday means
    /// Monday. The time is the clock's, rounded up to five minutes, because nobody writes 14:37.
    /// <para>
    /// Plain text, and only that. It used to be read back as a to-do's due date; a to-do is its
    /// own row now, so this stamps prose and nothing parses it.
    /// </para>
    /// </remarks>
    private void AppendDue(bool withTime)
    {
        if (DataContext is not MobileShellViewModel shell)
        {
            return;
        }

        (string text, int caret) = Read();
        int end = LineEnd(text, caret);

        // A line that already ends in a due suffix gets its replacement, not a second one.
        string line = text[LineStart(text, caret)..end];
        int existing = Daynote.App.Shell.Product.BodyHighlightSyntax.Pattern()
            .Matches(line)
            .Where(m => m.Groups["due"].Success && m.Index + m.Length == line.Length)
            .Select(m => m.Index)
            .DefaultIfEmpty(-1)
            .First();

        string suffix = withTime
            ? string.Format(
                CultureInfo.InvariantCulture,
                "({0}/{1} {2:00}:{3:00})",
                shell.SelectedDate.Month,
                shell.SelectedDate.Day,
                RoundedNow().Hour,
                RoundedNow().Minute)
            : string.Format(
                CultureInfo.InvariantCulture,
                "({0}/{1})",
                shell.SelectedDate.Month,
                shell.SelectedDate.Day);

        if (existing >= 0)
        {
            int at = LineStart(text, caret) + existing;
            Write(text.Remove(at, end - at).Insert(at, suffix), at + suffix.Length);
            return;
        }

        string spaced = end > LineStart(text, caret) && text[end - 1] != ' ' ? " " + suffix : suffix;
        Write(text.Insert(end, spaced), end + spaced.Length);
    }

    private static DateTime RoundedNow()
    {
        DateTime now = DateTime.Now;
        return now.AddMinutes(4 - ((now.Minute + 4) % 5)).AddSeconds(-now.Second);
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

    private static int LineStart(string text, int caret)
    {
        int index = text.LastIndexOf('\n', Math.Max(0, Math.Min(caret, text.Length) - 1));
        return index < 0 ? 0 : index + 1;
    }

    private static int LineEnd(string text, int caret)
    {
        int index = text.IndexOf('\n', Math.Min(caret, text.Length));
        return index < 0 ? text.Length : index;
    }
}
