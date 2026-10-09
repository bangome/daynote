using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
            previous.CaptureRequested -= OnCaptureRequested;
            previous.PropertyChanged -= OnShellPropertyChanged;
            previous.Capture.PropertyChanged -= OnCapturePropertyChanged;
        }

        _observed = DataContext as MobileShellViewModel;
        if (_observed is { } shell)
        {
            shell.TitleRenameStarted += OnTitleRenameStarted;
            shell.CaptureRequested += OnCaptureRequested;
            shell.PropertyChanged += OnShellPropertyChanged;
            shell.Capture.PropertyChanged += OnCapturePropertyChanged;
            PlaceCaptureHighlight();
        }
    }

    // ── Motion (spec M1, M2) ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// M1: the bar rises out of the toolbar's bottom edge the moment it opens, and the highlight
    /// slides between the two readings when the kind changes. The text itself never moves.
    /// </summary>
    private void OnCapturePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_observed is not { } shell || this.FindControl<StackPanel>("CaptureBar") is not { } bar)
        {
            return;
        }

        if (e.PropertyName == nameof(Daynote.App.Notes.AgendaCaptureViewModel.IsOpen))
        {
            PlaceCapturePopover();
        }

        if (e.PropertyName == nameof(Daynote.App.Notes.AgendaCaptureViewModel.IsOpen) && shell.Capture.IsOpen)
        {
            PlaceCaptureHighlight();
            Visual moving = _popover && this.FindControl<Border>("CapturePopover") is { } card ? card : bar;
            moving.Opacity = 0;
            Dispatcher.UIThread.Post(() =>
            {
                moving.Opacity = 1;
                PlaceCapturePopover();
                _ = MotionPlayer.Play(moving, "m1", _popover
                    ? Choreography.AtPopupOpen(moving)
                    : Choreography.AtBarRise(bar, bar.Bounds.Height + 8));
            }, DispatcherPriority.Loaded);
        }
        else if (e.PropertyName == nameof(Daynote.App.Notes.AgendaCaptureViewModel.Kind) &&
                 this.FindControl<Border>("CapHighlight") is { } highlight)
        {
            double to = HighlightY(shell.Capture.IsTaskSelected);
            _ = MotionPlayer.Play(highlight, "m1", Choreography.AtBarSwitch(highlight, HighlightY(!shell.Capture.IsTaskSelected), to));
        }
    }

    // ── The @ card, with a hardware keyboard (tablet §02) ─────────────────────────────────────────

    private bool _popover;

    /// <summary>
    /// A hardware keyboard came or went: the @ reading moves between the bar over the soft
    /// keyboard and the card under the caret. Whatever is being typed stays as it is.
    /// </summary>
    public void SetHardwareKeyboard(bool attached)
    {
        if (attached == _popover || this.FindControl<StackPanel>("CaptureBar") is not { } bar ||
            this.FindControl<Border>("CapturePopover") is not { } card)
        {
            return;
        }

        _popover = attached;
        if (attached && bar.Parent is Panel home)
        {
            _barHome = home;
            home.Children.Remove(bar);
            card.Child = bar;
        }
        else if (!attached && _barHome is { } original)
        {
            card.Child = null;
            original.Children.Add(bar);
        }

        if (this.FindControl<TextBlock>("CaptureHints") is { } hints)
        {
            hints.IsVisible = attached;
        }

        PlaceCapturePopover();
    }

    /// <summary>Where the bar lives when it is a bar, to put it back.</summary>
    private Panel? _barHome;

    /// <summary>
    /// Puts the card under the caret's line, or over it when the line is too near the bottom - the
    /// desktop's flip, growing from the edge nearest the caret.
    /// </summary>
    private void PlaceCapturePopover()
    {
        if (this.FindControl<Border>("CapturePopover") is not { } card || _observed is not { } shell)
        {
            return;
        }

        card.IsVisible = _popover && shell.Capture.IsOpen;
        if (!card.IsVisible || BodyBox is not { } body ||
            body.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.TextPresenter>().FirstOrDefault() is not { } presenter)
        {
            return;
        }

        Rect caret = presenter.TextLayout.HitTestTextPosition(Math.Clamp(body.CaretIndex, 0, (body.Text ?? string.Empty).Length));
        Point at = presenter.TranslatePoint(caret.BottomLeft, body) ?? default;
        double height = card.Bounds.Height > 0 ? card.Bounds.Height : 180;
        bool above = at.Y + 6 + height > body.Bounds.Height;
        double top = above ? at.Y - caret.Height - 6 - height : at.Y + 6;
        double left = Math.Clamp(at.X - 12, 8, Math.Max(8, body.Bounds.Width - card.Width - 8));
        card.Margin = new Thickness(left, Math.Max(0, top), 0, 0);
        card.RenderTransformOrigin = new RelativePoint(0, above ? 1 : 0, RelativeUnit.Relative);
    }

    /// <summary>The highlight under the task line, or under the event line one row (44 and the 2 between) down.</summary>
    private static double HighlightY(bool task) => task ? 0 : 46;

    private void PlaceCaptureHighlight()
    {
        if (_observed is { } shell && this.FindControl<Border>("CapHighlight") is { } highlight)
        {
            MotionTransform.For(highlight).Y = HighlightY(shell.Capture.IsTaskSelected);
        }
    }

    /// <summary>The ×: down and out (220 ms ease-in), and only then closed (M1).</summary>
    /// <remarks>
    /// The bar is still live while it leaves: a second × is ignored, and if Enter makes the item
    /// meanwhile, or the @ the bar was reading is gone or replaced by another, there is nothing
    /// left for this × to dismiss.
    /// </remarks>
    private async void OnDismissCapture(object? sender, RoutedEventArgs e)
    {
        if (_dismissing || DataContext is not MobileShellViewModel { Capture.IsOpen: true } shell)
        {
            return;
        }

        _dismissing = true;
        int reading = shell.Capture.AtIndex;
        try
        {
            if (this.FindControl<StackPanel>("CaptureBar") is { } bar)
            {
                await MotionPlayer.Play(bar, "m1", Choreography.AtBarDismiss(bar, bar.Bounds.Height + 8)).ConfigureAwait(true);
                MotionTransform.For(bar).Reset();
                bar.Opacity = 1;
            }

            if (shell.Capture.IsOpen && shell.Capture.AtIndex == reading)
            {
                shell.DismissCaptureCommand.Execute(null);
            }
        }
        finally
        {
            _dismissing = false;
        }
    }

    /// <summary>The × is playing its exit (M1); a second one waits for nothing.</summary>
    private bool _dismissing;

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
    /// The widget's @ 할 일: the caret at the end of the new note's body and an @ typed there, which
    /// is what opens the bar — the same as pressing the @ button, so the bar has one way in.
    /// </summary>
    /// <remarks>
    /// Posted at a low priority for the reason <see cref="FocusTitleBox"/> is: the editor has only
    /// just been asked to show, and a box that is not laid out yet takes neither focus nor a caret.
    /// </remarks>
    private void OnCaptureRequested(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (BodyBox is not { } box)
        {
            return;
        }

        box.Focus();
        box.CaretIndex = (box.Text ?? string.Empty).Length;
        OnInsertAt(sender, new RoutedEventArgs());
    }, DispatcherPriority.Background);

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
            PlaceCapturePopover();
        }
    }

    /// <summary>
    /// The return key, while the bar is up and has read something: it makes the item instead of a
    /// line break. The one key on a phone keyboard that can be given a second job, and only for as
    /// long as the bar is there to say so.
    /// </summary>
    private void OnBodyKeyDown(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        if (args is not KeyEventArgs e || DataContext is not MobileShellViewModel { Capture.IsOpen: true } shell)
        {
            return;
        }

        // A hardware keyboard has the desktop's other two keys (tablet T3): Tab switches the
        // reading and Esc lets it go, as the card's hint line says.
        switch (e.Key)
        {
            case Key.Enter when !shell.Capture.IsPrompting:
                e.Handled = true;
                shell.CommitCaptureCommand.Execute(null);
                break;
            case Key.Tab when !shell.Capture.IsPrompting:
                e.Handled = true;
                shell.Capture.ToggleKind();
                break;
            case Key.Escape:
                e.Handled = true;
                shell.DismissCaptureCommand.Execute(null);
                break;
        }
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
