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
    public EditorPage() => InitializeComponent();

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
    /// The whole page is extended rather than the toolbar alone: a child drawn past its page's
    /// bottom edge is clipped there, so the strip came out in the page colour.
    /// </remarks>
    public void SetBottomInset(double bottom)
    {
        Margin = new Thickness(0, 0, 0, -bottom);
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

    // ── The writing helpers ──────────────────────────────────────────────────────────────────────

    /// <summary>Opens the caret's line with an empty checkbox, or removes the one already there.</summary>
    private void OnInsertTodo(object? sender, RoutedEventArgs e)
    {
        (string text, int caret) = Read();
        int start = LineStart(text, caret);
        int indent = start;
        while (indent < text.Length && (text[indent] == ' ' || text[indent] == '\t'))
        {
            indent++;
        }

        const string Marker = "-[] ";
        string rest = text[indent..];

        // Tapping it again on a line that already has one takes it off, which is what a toggle in a
        // toolbar is expected to do and costs nothing to support.
        foreach (string existing in new[] { "-[] ", "-[ ] ", "-[x] ", "-[X] " })
        {
            if (rest.StartsWith(existing, StringComparison.Ordinal))
            {
                Write(text.Remove(indent, existing.Length), Math.Max(indent, caret - existing.Length));
                return;
            }
        }

        Write(text.Insert(indent, Marker), caret + Marker.Length);
    }

    /// <summary>Appends a due date to the caret's line: the note's own day, as <c>(M/D)</c>.</summary>
    private void OnInsertDate(object? sender, RoutedEventArgs e) => AppendDue(withTime: false);

    /// <summary>The same with a time, <c>(M/D H:mm)</c>, rounded to the next five minutes.</summary>
    private void OnInsertTime(object? sender, RoutedEventArgs e) => AppendDue(withTime: true);

    /// <remarks>
    /// The date is the note's own, not today's: a due suffix is read against the day the note
    /// belongs to, and someone writing up Monday on Monday means Monday. The time is the clock's,
    /// rounded up to five minutes, because nobody schedules anything for 14:37.
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
