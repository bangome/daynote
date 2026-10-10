using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using Daynote.Mobile.ViewModels;
using Daynote.Motion;

namespace Daynote.Mobile.Views;

/// <summary>
/// A to-do row that swipes: left uncovers 편집 and 삭제, right ticks it (or unticks a done one).
/// </summary>
/// <remarks>
/// <para>
/// The row is the one child given in XAML; what it uncovers is laid behind it here. A partial
/// swipe left springs open on the two buttons; past <see cref="Choreography.SwipeDeleteFraction"/>
/// of the width it deletes without stopping, with one light tap as the line is crossed. A swipe
/// right past <see cref="TickDistance"/> ticks through the row's own checkbox
/// (<see cref="TickButton.Tick"/>), so the motion, the held wait, the haptic and every refresh
/// after it are the tap's.
/// </para>
/// <para>
/// <b>Nothing is taken from a tap or a scroll.</b> A press only notes where it began; the row takes
/// the pointer once the finger has gone <see cref="DirectionLock"/> points sideways, and a press
/// that goes further up or down first is left to the scroll. A press within
/// <see cref="EdgeGuard"/> of either edge of the screen is the system's back gesture (Android takes
/// it from both) and is never a swipe.
/// </para>
/// <para>
/// A right-click, or a long press, opens the row on its buttons as a swipe would: a trackpad on an
/// iPad or a mouse on a tablet has no comfortable way to drag a row sideways.
/// </para>
/// <para>
/// One row is open at a time. A press anywhere else closes it, which is also what closes it when a
/// list starts to scroll.
/// </para>
/// </remarks>
public sealed class SwipeRow : Panel
{
    public static readonly StyledProperty<ICommand?> EditCommandProperty =
        AvaloniaProperty.Register<SwipeRow, ICommand?>(nameof(EditCommand));

    public static readonly StyledProperty<ICommand?> DeleteCommandProperty =
        AvaloniaProperty.Register<SwipeRow, ICommand?>(nameof(DeleteCommand));

    /// <summary>A repeating to-do asks which days first, so it does not leave before it is answered.</summary>
    public static readonly StyledProperty<bool> AsksBeforeDeleteProperty =
        AvaloniaProperty.Register<SwipeRow, bool>(nameof(AsksBeforeDelete));

    /// <summary>A done row's right swipe unticks it, and says so in grey rather than green.</summary>
    public static readonly StyledProperty<bool> IsDoneProperty =
        AvaloniaProperty.Register<SwipeRow, bool>(nameof(IsDone));

    /// <summary>How far, in points, a finger moves before the row decides between a swipe and a scroll.</summary>
    public const double DirectionLock = 10;

    /// <summary>A press this close to either edge of the screen is the system's back gesture.</summary>
    public const double EdgeGuard = 20;

    /// <summary>The width of each of the two buttons a left swipe opens on.</summary>
    public const double ButtonWidth = 76;

    /// <summary>How far right a row goes before letting go ticks it.</summary>
    public const double TickDistance = 88;

    private static SwipeRow? s_open;

    private readonly Border _tickPane;
    private readonly Glyph _tickMark;
    private readonly Grid _actions;
    private readonly Border _edit;
    private readonly Border _delete;
    private readonly TextBlock _editLabel;
    private readonly TextBlock _deleteLabel;

    private Point? _start;
    private bool _swiping;
    private double _base;
    private double _offset;
    private bool _armed;
    private TopLevel? _top;

    public SwipeRow()
    {
        _tickMark = new Glyph { Kind = GlyphKind.Check, Width = 18, Height = 18, Margin = new Thickness(24, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        // White on the green (or grey) in both themes, as the delete's label is on its red.
        _tickMark.Foreground = Brushes.White;
        _tickPane = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(12),
            ClipToBounds = true,
            Child = new Panel { HorizontalAlignment = HorizontalAlignment.Left, Children = { _tickMark } },
        };

        _editLabel = Label();
        _editLabel.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("Mobile.Text"));
        _edit = new Border { Child = _editLabel, ClipToBounds = true };
        _edit.Bind(Border.BackgroundProperty, this.GetResourceObservable("Mobile.Seg"));
        _edit.Tapped += (_, e) =>
        {
            e.Handled = true;
            Edit();
        };

        _deleteLabel = Label();
        _deleteLabel.Foreground = Brushes.White;
        _delete = new Border { Child = _deleteLabel, ClipToBounds = true };
        // Solid red in both themes: the dark theme's lighter danger red does not hold white text.
        _delete.Bind(Border.BackgroundProperty, this.GetResourceObservable("Mobile.DangerSolid"));
        _delete.Tapped += (_, e) =>
        {
            e.Handled = true;
            _ = DeleteAsync();
        };

        Grid.SetColumn(_delete, 1);
        _actions = new Grid
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            Children = { _edit, _delete },
        };

        var actionsClip = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            CornerRadius = new CornerRadius(12),
            ClipToBounds = true,
            Child = _actions,
        };
        Children.Add(new Panel { IsVisible = false, Children = { _tickPane, actionsClip } });

        // Hit-testable where the row is not: an open row's own content stops taking taps.
        Background = Brushes.Transparent;

        AddHandler(PointerPressedEvent, OnPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnMoved, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnReleased, handledEventsToo: true);
        ContextRequested += (_, e) =>
        {
            e.Handled = true;
            Settle(-OpenWidth);
        };
        AddHandler(PointerCaptureLostEvent, (_, _) =>
        {
            if (_swiping)
            {
                _swiping = false;
                _start = null;
                Settle(_offset <= -ButtonWidth ? -OpenWidth : 0);
            }
        });
    }

    public ICommand? EditCommand
    {
        get => GetValue(EditCommandProperty);
        set => SetValue(EditCommandProperty, value);
    }

    public ICommand? DeleteCommand
    {
        get => GetValue(DeleteCommandProperty);
        set => SetValue(DeleteCommandProperty, value);
    }

    public bool AsksBeforeDelete
    {
        get => GetValue(AsksBeforeDeleteProperty);
        set => SetValue(AsksBeforeDeleteProperty, value);
    }

    public bool IsDone
    {
        get => GetValue(IsDoneProperty);
        set => SetValue(IsDoneProperty, value);
    }

    /// <summary>True while it rests open on its two buttons.</summary>
    public bool IsOpen => s_open == this;

    /// <summary>Where the row is, in points: negative uncovers the buttons, positive the tick.</summary>
    public double Offset => _offset;

    /// <summary>The row itself, the child given in XAML.</summary>
    private Control? Row => Children.Count > 1 ? Children[1] : null;

    private Panel Behind => (Panel)Children[0];

    private double OpenWidth => ButtonWidth * 2;

    private double Width0 => Math.Max(Bounds.Width, 1);

    /// <summary>Closes whichever row is open, if any.</summary>
    public static void CloseOpen() => s_open?.Settle(0);

    /// <summary>Puts the row at <paramref name="offset"/> at once, as a finger would: for rendering a half-swiped row.</summary>
    public void ShowAt(double offset)
    {
        MotionPlayer.Stop(this, "swipe");
        Place(offset);
        if (offset < 0 && offset > -Width0 * Choreography.SwipeDeleteFraction)
        {
            MarkOpen(offset <= -OpenWidth / 2);
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _editLabel.Text = MobileStrings.Get("MobileTodoEdit");
        _deleteLabel.Text = MobileStrings.Get("MobileTodoDelete");
        _top = TopLevel.GetTopLevel(this);
        _top?.AddHandler(PointerPressedEvent, OnAnywherePressed, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _top?.RemoveHandler(PointerPressedEvent, OnAnywherePressed);
        _top = null;
        if (s_open == this)
        {
            s_open = null;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DataContextProperty)
        {
            // A rebuilt list hands an old row a new to-do: it starts closed.
            MotionPlayer.Stop(this, "swipe");
            Height = double.NaN;
            Opacity = 1;
            Place(0);
            MarkOpen(false);
        }
    }

    /// <summary>A press anywhere outside the open row closes it - the start of a scroll included.</summary>
    private void OnAnywherePressed(object? sender, PointerPressedEventArgs e)
    {
        if (s_open == this && e.Source is Visual source && !this.IsVisualAncestorOf(source) && source != this)
        {
            Settle(0);
        }
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        _start = null;
        _swiping = false;
        if (Row is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.Pointer.Type == PointerType.Mouse)
        {
            return;
        }

        // The system's back gesture starts at either edge; it is never a row's swipe.
        if (TopLevel.GetTopLevel(this) is { } top &&
            (e.GetPosition(top).X < EdgeGuard || e.GetPosition(top).X > top.Bounds.Width - EdgeGuard))
        {
            return;
        }

        if (e.Source is Visual source && _actions.IsVisualAncestorOf(source))
        {
            return;
        }

        _start = e.GetPosition(this);
        _base = _offset;
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_start is not { } start)
        {
            return;
        }

        Point now = e.GetPosition(this);
        double dx = now.X - start.X;
        double dy = now.Y - start.Y;
        if (!_swiping)
        {
            if (Math.Abs(dx) < DirectionLock && Math.Abs(dy) < DirectionLock)
            {
                return;
            }

            if (Math.Abs(dy) >= Math.Abs(dx))
            {
                // Up or down first: a scroll, and none of this row's business.
                _start = null;
                return;
            }

            // Sideways: the row takes the pointer, which also keeps the checkbox or the row under
            // the finger from treating the release as its click.
            _swiping = true;
            if (s_open is { } other && other != this)
            {
                other.Settle(0);
            }

            MotionPlayer.Stop(this, "swipe");
            e.Pointer.Capture(this);
        }

        e.Handled = true;
        double offset = _base + dx;
        double width = Width0;

        // Right stops short of the far edge; it only has to say "done".
        offset = Math.Clamp(offset, -width, width * 0.45);
        Place(offset);

        bool armed = offset <= -width * Choreography.SwipeDeleteFraction || offset >= TickDistance;
        if (armed && !_armed)
        {
            MotionEnvironment.Haptic(HapticKind.Selection);
        }

        _armed = armed;
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        bool swiped = _swiping;
        Point? start = _start;
        _start = null;
        _swiping = false;
        _armed = false;
        if (!swiped)
        {
            // A tap on an open row closes it rather than doing what the row does.
            if (start is not null && IsOpen)
            {
                e.Handled = true;
                Settle(0);
            }

            return;
        }

        e.Pointer.Capture(null);
        e.Handled = true;
        if (_offset <= -Width0 * Choreography.SwipeDeleteFraction)
        {
            _ = DeleteAsync();
        }
        else if (_offset < 0)
        {
            Settle(_offset <= -OpenWidth / 2 ? -OpenWidth : 0);
        }
        else if (_offset >= TickDistance)
        {
            Settle(0);
            Row?.GetVisualDescendants().OfType<TickButton>().FirstOrDefault()?.Tick();
        }
        else
        {
            Settle(0);
        }
    }

    private void Edit()
    {
        Settle(0);
        if (EditCommand is { } edit && edit.CanExecute(null))
        {
            edit.Execute(null);
        }
    }

    /// <summary>
    /// 삭제, from the button or a full swipe. A one-off leaves at once - off the edge, and its room
    /// closes - and the command runs after; a repeating one closes and asks first.
    /// </summary>
    private async Task DeleteAsync()
    {
        if (DeleteCommand is not { } delete || !delete.CanExecute(null))
        {
            Settle(0);
            return;
        }

        if (AsksBeforeDelete)
        {
            Settle(0);
            delete.Execute(null);
            return;
        }

        MarkOpen(false);
        await MotionPlayer.Play(this, "swipe", Choreography.RowDelete(this, Place, _offset, Width0, Bounds.Height))
            .ConfigureAwait(true);
        if (delete is CommunityToolkit.Mvvm.Input.IAsyncRelayCommand async)
        {
            await async.ExecuteAsync(null).ConfigureAwait(true);
        }
        else
        {
            delete.Execute(null);
        }

        // The list is rebuilt without it. Still here means nothing was deleted: it comes back.
        if (this.IsAttachedToVisualTree())
        {
            Height = double.NaN;
            Opacity = 1;
            Place(0);
        }
    }

    /// <summary>Springs to <paramref name="to"/>: open on the buttons, or closed.</summary>
    private void Settle(double to)
    {
        MarkOpen(to < 0);
        if (Math.Abs(_offset - to) < 0.5)
        {
            Place(to);
            return;
        }

        _ = MotionPlayer.Play(this, "swipe", Choreography.SwipeSettle(Place, _offset, to));
    }

    private void MarkOpen(bool open)
    {
        if (open)
        {
            if (s_open is { } other && other != this)
            {
                other.Settle(0);
            }

            s_open = this;
        }
        else if (s_open == this)
        {
            s_open = null;
        }

        // While open, a tap on the row is a tap to close it, not one on what the row holds.
        if (Row is { } row)
        {
            row.IsHitTestVisible = !open;
        }
    }

    /// <summary>Moves the row and sizes what it uncovers to exactly the gap it leaves.</summary>
    private void Place(double offset)
    {
        _offset = offset;
        if (Row is { } row)
        {
            MotionTransform.For(row).X = offset;
        }

        // Clipped only while it moves: at rest the checkbox's pulse ring is allowed past the row.
        ClipToBounds = offset != 0;
        Behind.IsVisible = offset != 0;
        _tickPane.Width = Math.Max(0, offset);
        _tickPane.IsVisible = offset > 0;
        _actions.Width = Math.Max(0, -offset);
        _actions.IsVisible = offset < 0;
        if (offset > 0)
        {
            _tickPane.Background = (IsDone
                ? this.FindResource("Mobile.Text3")
                : this.FindResource("Mobile.Saved")) as IBrush;
            _tickMark.Opacity = Math.Clamp(offset / TickDistance, 0.3, 1);
        }

        // Past the line the delete takes the whole width: letting go now deletes.
        bool deleting = offset <= -Width0 * Choreography.SwipeDeleteFraction;
        _actions.ColumnDefinitions[0].Width = deleting ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
    }

    private static TextBlock Label() => new()
    {
        FontSize = 14,
        FontWeight = FontWeight.Bold,
        TextWrapping = TextWrapping.NoWrap,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
}
