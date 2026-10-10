using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Daynote.Mobile.ViewModels;
using Daynote.Motion;

namespace Daynote.Mobile.Views;

/// <summary>
/// The tablet's day panel: the day's open to-dos, and the finished ones behind their count.
/// </summary>
/// <remarks>
/// The shell keeps one list of the day's rows, open then done; the panel splits it, because it
/// folds the done ones away where the phone's day screen lists them all. The split is a view of
/// the same row objects, so a row keeps its key and M2 can tell a new one from an old one.
/// </remarks>
public partial class DayTodoPanel : UserControl
{
    private readonly ObservableCollection<TodoRowViewModel> _open = [];
    private readonly ObservableCollection<TodoRowViewModel> _done = [];
    private MobileShellViewModel? _observed;
    private bool _splitQueued;
    private bool _doneShown;

    /// <summary>When the M4 line closes on its own; moved on while a pointer rests on it.</summary>
    private DispatcherTimer? _noticeTimer;

    public DayTodoPanel()
    {
        InitializeComponent();
        if (this.FindControl<ItemsControl>("OpenList") is { } open)
        {
            open.ItemsSource = _open;

            // An unticked row comes back from the done fold; it is not a new to-do arriving (M2).
            Daynote.Motion.RowArrivals.SetKnownItems(open, _done);
        }

        if (this.FindControl<ItemsControl>("DoneList") is { } done)
        {
            done.ItemsSource = _done;
        }

        if (this.FindControl<Border>("Notice") is { } notice)
        {
            // M4 on the desktop: the line stays while the pointer is on it.
            notice.PointerEntered += (_, _) => _noticeTimer?.Stop();
            notice.PointerExited += (_, _) => _noticeTimer?.Start();
        }
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_observed is { } previous)
        {
            previous.DayTodos.CollectionChanged -= OnDayTodosChanged;
            previous.PropertyChanged -= OnShellPropertyChanged;
        }

        _observed = DataContext as MobileShellViewModel;
        if (_observed is { } shell)
        {
            shell.DayTodos.CollectionChanged += OnDayTodosChanged;
            shell.PropertyChanged += OnShellPropertyChanged;
        }

        Split();
    }

    private void OnDayTodosChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // A rebuild is a clear and a run of adds; split once, after the last.
        if (!_splitQueued)
        {
            _splitQueued = true;
            Dispatcher.UIThread.Post(Split);
        }
    }

    private void Split()
    {
        _splitQueued = false;
        _open.Clear();
        _done.Clear();
        foreach (TodoRowViewModel row in _observed?.DayTodos ?? [])
        {
            (row.Item.Checked ? _done : _open).Add(row);
        }

        if (this.FindControl<TextBlock>("LeftCount") is { } left)
        {
            left.Text = MobileStrings.Format("MobileWideTodoLeftFormat", _open.Count);
        }

        if (this.FindControl<TextBlock>("DoneLabel") is { } label)
        {
            label.Text = (_doneShown ? "⌄ " : "› ") + MobileStrings.Format("MobileWideDoneFormat", _done.Count);
        }

        SetVisible("OpenList", _open.Count > 0);
        SetVisible("Empty", _open.Count == 0);
        SetVisible("DoneToggle", _done.Count > 0);
        SetVisible("DoneList", _doneShown && _done.Count > 0);
    }

    private void SetVisible(string name, bool visible)
    {
        if (this.FindControl<Control>(name) is { } control)
        {
            control.IsVisible = visible;
        }
    }

    private void OnDoneToggle(object? sender, RoutedEventArgs e)
    {
        _doneShown = !_doneShown;
        Split();
    }

    // ── M4: "added to another date" ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Something just made in the to-do sheet went to another date: the line opens at the top of
    /// the panel, holds four seconds (longer under a pointer) and closes slowly.
    /// </summary>
    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MobileShellViewModel.MadeElsewhereText) ||
            _observed is not { MadeElsewhereText: { } made } ||
            !IsEffectivelyVisible ||
            this.FindControl<Border>("Notice") is not { } notice)
        {
            return;
        }

        if (this.FindControl<TextBlock>("NoticeText") is { } text)
        {
            text.Text = made;
        }

        notice.IsVisible = true;
        _ = MotionPlayer.Play(notice, "m4", Choreography.NoticeOpen(notice, 36));

        _noticeTimer?.Stop();
        _noticeTimer = new DispatcherTimer { Interval = Choreography.NoticeHold };
        _noticeTimer.Tick += (_, _) => _ = CloseNoticeAsync();
        _noticeTimer.Start();
    }

    private async Task CloseNoticeAsync()
    {
        _noticeTimer?.Stop();
        _noticeTimer = null;
        if (this.FindControl<Border>("Notice") is { IsVisible: true } notice &&
            await MotionPlayer.Play(notice, "m4", Choreography.NoticeClose(notice, 36)).ConfigureAwait(true))
        {
            notice.IsVisible = false;
            notice.Height = double.NaN;
            notice.Opacity = 1;
        }
    }

    /// <summary>보기: the line closes and the day it went to comes up (M4 into M5).</summary>
    private void OnNoticeView(object? sender, RoutedEventArgs e)
    {
        _ = CloseNoticeAsync();
        _observed?.ViewMadeElsewhereCommand.Execute(null);
    }
}
