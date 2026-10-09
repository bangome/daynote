using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Text;
using Android.Text.Style;
using Android.Views;
using Android.Widget;
using Daynote.Mobile.ViewModels;
using Daynote.Mobile.Widgets;

namespace Daynote.Mobile.Android.Platform.Widgets;

/// <summary>
/// Draws the four home-screen widgets (design §03): 4×2 오늘, 2×2 다음 일정, 2×1 빠른 기록 and
/// 4×4 하루, all from one <see cref="WidgetSnapshot"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>RemoteViews, not Glance.</b> Glance is Kotlin and Compose; this app has neither. Rows are
/// added as child RemoteViews rather than served by a RemoteViewsService: a widget shows three or
/// five of them, the header already says how many are left, and a collection would cost a service
/// and a mutable PendingIntent template for nothing a fixed list cannot do.
/// </para>
/// <para>
/// <b>When it redraws.</b> When the app says a to-do changed (<see cref="RequestUpdate"/>, from
/// the one place every change passes), when the system asks (placing, resizing, boot, an update of
/// the app), on a clock or zone change, and at the next moment the snapshot would look different
/// on its own (<see cref="WidgetSnapshot.NextRefresh"/>) by an inexact alarm that does not wake the
/// phone. No <c>updatePeriodMillis</c>: polling would only ever find what one of those already did.
/// </para>
/// <para>
/// <b>No animation</b> (motion M8, Android): a tick swaps the row to done at once, and the next
/// draw after <see cref="SettleFor"/> drops it.
/// </para>
/// </remarks>
internal static class DaynoteWidgets
{
    internal const string ExtraLaunch = "daynote.widget.launch";
    internal const string ExtraKey = "daynote.widget.key";
    internal const string ExtraDay = "daynote.widget.day";

    /// <summary>How long a row ticked on the widget stays drawn, done, before it drops.</summary>
    internal static readonly TimeSpan SettleFor = TimeSpan.FromSeconds(2);

    private const int TodayRows = 3;
    private const int DayRows = 5;

    private static readonly SemaphoreSlim Drawing = new(1, 1);
    private static readonly Lock SettlingLock = new();
    private static readonly Dictionary<WidgetRowKey, (DateOnly Day, DateTime Until)> Settling = [];
    private static int _queued;

    /// <summary>
    /// Redraws every placed widget, soon and off the UI thread. A burst of calls (the app refreshes
    /// its to-dos several times around one edit) costs one draw.
    /// </summary>
    internal static void RequestUpdate(Context context)
    {
        if (Interlocked.Exchange(ref _queued, 1) == 1)
        {
            return;
        }

        Context app = context.ApplicationContext ?? context;
        _ = Task.Run(async () =>
        {
            await Task.Delay(300).ConfigureAwait(false);
            Interlocked.Exchange(ref _queued, 0);
            try
            {
                await UpdateAllAsync(app).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // A profile switch can close the database under a draw; the next change redraws.
                global::Android.Util.Log.Warn("Daynote", $"Widget update failed: {exception}");
            }
        });
    }

    /// <summary>Runs <paramref name="work"/> past the end of <c>OnReceive</c>, as a receiver may.</summary>
    internal static void RunAsync(BroadcastReceiver receiver, Func<Task> work)
    {
        BroadcastReceiver.PendingResult? pending = receiver.GoAsync();
        _ = Task.Run(async () =>
        {
            try
            {
                await work().ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                global::Android.Util.Log.Warn("Daynote", $"Widget work failed: {exception}");
            }
            finally
            {
                pending?.Finish();
            }
        });
    }

    /// <summary>A ring was tapped: tick it, show it done at once, then drop it.</summary>
    internal static async Task ToggleAsync(Context context, WidgetRowKey key, DateOnly day)
    {
        bool ticked = await WidgetData.ToggleAsync(
            AndroidPlatformServices.ResolveDataRoot(context), new AndroidKeyStoreSecretProtector(), key, day)
            .ConfigureAwait(false);
        if (!ticked)
        {
            await UpdateAllAsync(context).ConfigureAwait(false);
            return;
        }

        lock (SettlingLock)
        {
            Settling[key] = (day, DateTime.UtcNow + SettleFor);
        }

        await UpdateAllAsync(context).ConfigureAwait(false);
        await Task.Delay(SettleFor).ConfigureAwait(false);
        await UpdateAllAsync(context).ConfigureAwait(false);
    }

    /// <summary>Reads the store once and redraws every placed widget of all four kinds.</summary>
    internal static async Task UpdateAllAsync(Context context)
    {
        var manager = AppWidgetManager.GetInstance(context);
        if (manager is null)
        {
            return;
        }

        int[] today = Ids(context, manager, typeof(TodayWidgetProvider));
        int[] next = Ids(context, manager, typeof(NextEventWidgetProvider));
        int[] capture = Ids(context, manager, typeof(CaptureWidgetProvider));
        int[] day = Ids(context, manager, typeof(DayWidgetProvider));
        if (today.Length + next.Length + capture.Length + day.Length == 0)
        {
            return;
        }

        await Drawing.WaitAsync().ConfigureAwait(false);
        try
        {
            // .NET caches the zone; after a zone change the cached one is the old one.
            TimeZoneInfo.ClearCachedData();
            DateTime now = DateTime.Now;
            WidgetSnapshot snapshot = await WidgetData.ReadAsync(
                AndroidPlatformServices.ResolveDataRoot(context),
                new AndroidKeyStoreSecretProtector(),
                now,
                CurrentSettling()).ConfigureAwait(false);

            if (today.Length > 0)
            {
                manager.UpdateAppWidget(today, Today(context, snapshot));
            }

            if (next.Length > 0)
            {
                manager.UpdateAppWidget(next, Next(context, snapshot));
            }

            if (capture.Length > 0)
            {
                manager.UpdateAppWidget(capture, Capture(context, snapshot));
            }

            if (day.Length > 0)
            {
                manager.UpdateAppWidget(day, Day(context, snapshot));
            }

            ScheduleRefresh(context, snapshot.NextRefresh);
        }
        finally
        {
            Drawing.Release();
        }
    }

    private static int[] Ids(Context context, AppWidgetManager manager, Type provider) =>
        manager.GetAppWidgetIds(new ComponentName(context, Java.Lang.Class.FromType(provider))) ?? [];

    private static List<WidgetSettling> CurrentSettling()
    {
        lock (SettlingLock)
        {
            DateTime now = DateTime.UtcNow;
            foreach (WidgetRowKey gone in Settling.Where(entry => entry.Value.Until <= now).Select(entry => entry.Key).ToList())
            {
                Settling.Remove(gone);
            }

            return [.. Settling.Select(entry => new WidgetSettling(entry.Key, entry.Value.Day))];
        }
    }

    private static RemoteViews Today(Context context, WidgetSnapshot snapshot)
    {
        var views = new RemoteViews(context.PackageName, Resource.Layout.widget_today);
        views.SetTextViewText(Resource.Id.widget_title, snapshot.Text("WidgetToday"));
        views.SetTextViewText(
            Resource.Id.widget_remaining,
            snapshot.State == WidgetState.Locked ? string.Empty : snapshot.Format("WidgetRemainingFormat", snapshot.Remaining));
        AddButton(context, views, snapshot);
        views.SetOnClickPendingIntent(Resource.Id.widget_header, Launch(context, WidgetLaunch.Today));
        Rows(context, views, snapshot, TodayRows, more: false);
        return views;
    }

    private static RemoteViews Day(Context context, WidgetSnapshot snapshot)
    {
        var views = new RemoteViews(context.PackageName, Resource.Layout.widget_day);
        views.SetTextViewText(Resource.Id.widget_title, snapshot.DateHeader);
        AddButton(context, views, snapshot);
        views.SetOnClickPendingIntent(Resource.Id.widget_header, Launch(context, WidgetLaunch.Today));

        views.RemoveAllViews(Resource.Id.widget_week);
        foreach (WidgetWeekday weekday in snapshot.Week)
        {
            var cell = new RemoteViews(
                context.PackageName, weekday.IsToday ? Resource.Layout.widget_weekday_today : Resource.Layout.widget_weekday);
            cell.SetTextViewText(Resource.Id.widget_weekday_name, weekday.Name);
            cell.SetTextViewText(
                Resource.Id.widget_weekday_number, weekday.Number.ToString(System.Globalization.CultureInfo.InvariantCulture));
            views.AddView(Resource.Id.widget_week, cell);
        }

        if (snapshot.NextEvent is { } next)
        {
            views.SetViewVisibility(Resource.Id.widget_event, ViewStates.Visible);
            views.SetTextViewText(Resource.Id.widget_event_title, next.Title);
            views.SetTextViewText(Resource.Id.widget_event_span, next.Span);
            views.SetOnClickPendingIntent(Resource.Id.widget_event, Launch(context, WidgetLaunch.Today));
        }
        else
        {
            views.SetViewVisibility(Resource.Id.widget_event, ViewStates.Gone);
        }

        Rows(context, views, snapshot, DayRows, more: true);
        return views;
    }

    private static RemoteViews Next(Context context, WidgetSnapshot snapshot)
    {
        var views = new RemoteViews(context.PackageName, Resource.Layout.widget_next);
        views.SetTextViewText(Resource.Id.widget_title, snapshot.Text("WidgetUpNext"));
        views.SetOnClickPendingIntent(Resource.Id.widget_root, Launch(context, WidgetLaunch.Today));

        WidgetEvent? next = snapshot.State == WidgetState.Locked ? null : snapshot.NextEvent;
        views.SetViewVisibility(Resource.Id.widget_event_when, next is null ? ViewStates.Gone : ViewStates.Visible);
        views.SetViewVisibility(Resource.Id.widget_event_span, next is null ? ViewStates.Gone : ViewStates.Visible);
        views.SetTextViewText(Resource.Id.widget_event_when, next?.When ?? string.Empty);
        views.SetTextViewText(Resource.Id.widget_event_span, next?.Span ?? string.Empty);
        views.SetTextViewText(
            Resource.Id.widget_event_title,
            next?.Title ?? snapshot.Text(snapshot.State == WidgetState.Locked ? "WidgetLocked" : "WidgetNoEvent"));
        return views;
    }

    private static RemoteViews Capture(Context context, WidgetSnapshot snapshot)
    {
        var views = new RemoteViews(context.PackageName, Resource.Layout.widget_capture);
        views.SetTextViewText(Resource.Id.widget_new_note, snapshot.Text("WidgetNewNote"));
        views.SetTextViewText(Resource.Id.widget_new_todo, snapshot.Text("WidgetNewTodo"));
        views.SetOnClickPendingIntent(Resource.Id.widget_new_note, Launch(context, WidgetLaunch.NewNote));
        views.SetOnClickPendingIntent(Resource.Id.widget_new_todo, Launch(context, WidgetLaunch.Capture));
        return views;
    }

    private static void AddButton(Context context, RemoteViews views, WidgetSnapshot snapshot)
    {
        views.SetOnClickPendingIntent(Resource.Id.widget_add, Launch(context, WidgetLaunch.Capture));
        views.SetContentDescription(Resource.Id.widget_add, snapshot.Text("WidgetAddTodo"));
    }

    /// <summary>The rows, or the one line that stands in for them: empty, or locked.</summary>
    private static void Rows(Context context, RemoteViews views, WidgetSnapshot snapshot, int room, bool more)
    {
        views.RemoveAllViews(Resource.Id.widget_rows);
        string message = snapshot.State == WidgetState.Locked ? snapshot.Text("WidgetLocked")
            : snapshot.Todos.Count == 0 ? snapshot.Text("WidgetEmpty")
            : string.Empty;
        views.SetTextViewText(Resource.Id.widget_message, message);
        views.SetViewVisibility(Resource.Id.widget_message, message.Length > 0 ? ViewStates.Visible : ViewStates.Gone);
        if (message.Length > 0)
        {
            if (more)
            {
                views.SetViewVisibility(Resource.Id.widget_more, ViewStates.Gone);
            }

            return;
        }

        int shown = Math.Min(room, snapshot.Todos.Count);
        for (int i = 0; i < shown; i++)
        {
            views.AddView(Resource.Id.widget_rows, Row(context, snapshot, snapshot.Todos[i], i, shown));
        }

        if (more)
        {
            int hidden = snapshot.Todos.Count - shown;
            views.SetViewVisibility(Resource.Id.widget_more, hidden > 0 ? ViewStates.Visible : ViewStates.Gone);
            views.SetTextViewText(Resource.Id.widget_more, hidden > 0 ? snapshot.Format("WidgetMoreFormat", hidden) : string.Empty);
        }
    }

    private static RemoteViews Row(Context context, WidgetSnapshot snapshot, WidgetTodo todo, int index, int count)
    {
        var row = new RemoteViews(context.PackageName, Resource.Layout.widget_row);

        // Material 3 grouping: large corners only where the group starts and ends.
        int background = count == 1 ? Resource.Drawable.widget_row_single
            : index == 0 ? Resource.Drawable.widget_row_first
            : index == count - 1 ? Resource.Drawable.widget_row_last
            : Resource.Drawable.widget_row_middle;
        row.SetInt(Resource.Id.widget_row, "setBackgroundResource", background);

        row.SetImageViewResource(Resource.Id.widget_row_ring, todo.IsDone ? Resource.Drawable.widget_ring_done : Resource.Drawable.widget_ring);
        row.SetInt(Resource.Id.widget_row_ring, "setColorFilter", unchecked((int)WidgetSnapshot.ListColors[todo.ListColor]));

        if (todo.IsDone)
        {
            var struck = new SpannableString(todo.Title);
            struck.SetSpan(new StrikethroughSpan(), 0, todo.Title.Length, SpanTypes.ExclusiveExclusive);
            row.SetTextViewText(Resource.Id.widget_row_title_done, struck);
        }
        else
        {
            row.SetTextViewText(Resource.Id.widget_row_title, todo.Title);
        }

        row.SetViewVisibility(Resource.Id.widget_row_title, todo.IsDone ? ViewStates.Gone : ViewStates.Visible);
        row.SetViewVisibility(Resource.Id.widget_row_title_done, todo.IsDone ? ViewStates.Visible : ViewStates.Gone);
        row.SetViewVisibility(Resource.Id.widget_row_repeat, todo.IsRepeat ? ViewStates.Visible : ViewStates.Gone);

        int when = todo.IsOverdue ? Resource.Id.widget_row_when_overdue : Resource.Id.widget_row_when;
        row.SetTextViewText(when, todo.When);
        row.SetViewVisibility(Resource.Id.widget_row_when, todo.IsOverdue || todo.When.Length == 0 ? ViewStates.Gone : ViewStates.Visible);
        row.SetViewVisibility(Resource.Id.widget_row_when_overdue, todo.IsOverdue && todo.When.Length > 0 ? ViewStates.Visible : ViewStates.Gone);

        row.SetContentDescription(
            Resource.Id.widget_row,
            snapshot.Format(todo.IsDone ? "WidgetReopenFormat" : "WidgetCompleteFormat", todo.Title));
        row.SetOnClickPendingIntent(Resource.Id.widget_row, Toggle(context, todo));
        return row;
    }

    /// <summary>
    /// The tick, as a broadcast to <see cref="WidgetToggleReceiver"/>. The row's key is also the
    /// intent's data, so every row has a PendingIntent of its own.
    /// </summary>
    private static PendingIntent Toggle(Context context, WidgetTodo todo)
    {
        var intent = new Intent(context, typeof(WidgetToggleReceiver));
        intent.SetData(global::Android.Net.Uri.Parse($"daynote-widget:toggle/{todo.Key}/{todo.Day:yyyy-MM-dd}"));
        intent.PutExtra(ExtraKey, todo.Key.ToString());
        intent.PutExtra(ExtraDay, todo.Day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        return PendingIntent.GetBroadcast(context, 0, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
    }

    /// <summary>Brings <see cref="MainActivity"/> up with where the tap should land.</summary>
    private static PendingIntent Launch(Context context, WidgetLaunch launch)
    {
        var intent = new Intent(context, typeof(MainActivity));
        intent.SetData(global::Android.Net.Uri.Parse($"daynote-widget:launch/{launch}"));
        intent.PutExtra(ExtraLaunch, launch.ToString());
        intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop);
        return PendingIntent.GetActivity(context, 0, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
    }

    /// <summary>
    /// Arms the one redraw nothing else would cause. Inexact and not a wakeup: a widget nobody is
    /// looking at can wait for the screen to come on, and a minute or two late is invisible.
    /// </summary>
    private static void ScheduleRefresh(Context context, DateTime at)
    {
        if (context.GetSystemService(Context.AlarmService) is not AlarmManager alarms)
        {
            return;
        }

        var intent = new Intent(context, typeof(WidgetRefreshReceiver));
        intent.SetAction(WidgetRefreshReceiver.ActionRefresh);
        PendingIntent pending = PendingIntent.GetBroadcast(
            context, 0, intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable)!;
        long trigger = new DateTimeOffset(at, TimeZoneInfo.Local.GetUtcOffset(at)).ToUnixTimeMilliseconds();
        alarms.SetWindow(AlarmType.Rtc, trigger, (long)TimeSpan.FromMinutes(1).TotalMilliseconds, pending);
    }
}
