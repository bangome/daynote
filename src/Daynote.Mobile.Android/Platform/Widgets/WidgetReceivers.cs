using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.OS;
using Daynote.Mobile.Widgets;

namespace Daynote.Mobile.Android.Platform.Widgets;

/// <summary>
/// The four providers. Each only names its layout and picker entry (Resources/xml/widget_*_info.xml);
/// they all redraw together, from one read of the store, because a change that moves one moves the
/// others.
/// </summary>
/// <remarks>
/// Exported, as an app widget provider has to be for the launcher's host to bind it. All one can be
/// sent from outside is "redraw".
/// </remarks>
public abstract class DaynoteWidgetProvider : AppWidgetProvider
{
    public override void OnUpdate(Context? context, AppWidgetManager? appWidgetManager, int[]? appWidgetIds)
    {
        if (context is not null)
        {
            DaynoteWidgets.RunAsync(this, () => DaynoteWidgets.UpdateAllAsync(context));
        }
    }

    /// <summary>Resized: the launcher re-inflates, and the rows have to be put back in.</summary>
    public override void OnAppWidgetOptionsChanged(Context? context, AppWidgetManager? appWidgetManager, int appWidgetId, Bundle? newOptions)
    {
        if (context is not null)
        {
            DaynoteWidgets.RunAsync(this, () => DaynoteWidgets.UpdateAllAsync(context));
        }
    }
}

[BroadcastReceiver(Label = "@string/widget_today_label", Exported = true)]
[IntentFilter([AppWidgetManager.ActionAppwidgetUpdate])]
[MetaData(AppWidgetManager.MetaDataAppwidgetProvider, Resource = "@xml/widget_today_info")]
public sealed class TodayWidgetProvider : DaynoteWidgetProvider;

[BroadcastReceiver(Label = "@string/widget_next_label", Exported = true)]
[IntentFilter([AppWidgetManager.ActionAppwidgetUpdate])]
[MetaData(AppWidgetManager.MetaDataAppwidgetProvider, Resource = "@xml/widget_next_info")]
public sealed class NextEventWidgetProvider : DaynoteWidgetProvider;

[BroadcastReceiver(Label = "@string/widget_capture_label", Exported = true)]
[IntentFilter([AppWidgetManager.ActionAppwidgetUpdate])]
[MetaData(AppWidgetManager.MetaDataAppwidgetProvider, Resource = "@xml/widget_capture_info")]
public sealed class CaptureWidgetProvider : DaynoteWidgetProvider;

[BroadcastReceiver(Label = "@string/widget_day_label", Exported = true)]
[IntentFilter([AppWidgetManager.ActionAppwidgetUpdate])]
[MetaData(AppWidgetManager.MetaDataAppwidgetProvider, Resource = "@xml/widget_day_info")]
public sealed class DayWidgetProvider : DaynoteWidgetProvider;

/// <summary>
/// A ring tapped on a widget. Not exported: only the app's own PendingIntents reach it.
/// </summary>
[BroadcastReceiver(Exported = false)]
public sealed class WidgetToggleReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null
            || WidgetRowKey.Parse(intent?.GetStringExtra(DaynoteWidgets.ExtraKey)) is not { } key
            || !DateOnly.TryParseExact(
                intent?.GetStringExtra(DaynoteWidgets.ExtraDay),
                "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None,
                out DateOnly day))
        {
            return;
        }

        // Absent only on an intent from a build before the state was carried: finishing is what a
        // tap on an open ring meant then too.
        bool complete = intent?.GetBooleanExtra(DaynoteWidgets.ExtraComplete, true) ?? true;
        DaynoteWidgets.RunAsync(this, () => DaynoteWidgets.SetDoneAsync(context, key, day, complete));
    }
}

/// <summary>
/// Redraws when the widgets would otherwise go stale: the alarm <see cref="DaynoteWidgets"/> arms
/// for midnight or the next change, a clock or time-zone change (which moves "today" and every
/// overdue line), a language change of the phone, and an update of the app.
/// </summary>
/// <remarks>
/// Not exported: the system's broadcasts reach a non-exported receiver, and the alarm is the app's
/// own. The manifest filter carries the system actions; the alarm names this class directly.
/// </remarks>
[BroadcastReceiver(Exported = false)]
[IntentFilter([
    Intent.ActionTimeChanged,
    Intent.ActionTimezoneChanged,
    Intent.ActionLocaleChanged,
    Intent.ActionMyPackageReplaced,
])]
public sealed class WidgetRefreshReceiver : BroadcastReceiver
{
    internal const string ActionRefresh = "cc.arachat.daynote.widget.REFRESH";

    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is not null)
        {
            DaynoteWidgets.RunAsync(this, () => DaynoteWidgets.UpdateAllAsync(context));
        }
    }
}
