using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Daynote.App.Shell.Product;
using Daynote.Motion;
using Daynote.Mobile.ViewModels;

namespace Daynote.Mobile.Views;

/// <summary>
/// Plays M2 on a to-do list: a row that was not there before slides into a room it opens, and its
/// orange background holds a moment and drains.
/// </summary>
/// <remarks>
/// <para>
/// The lists are rebuilt whole on every change, so "new" cannot be read off the collection; it is
/// a key (<see cref="TodoItemViewModel.Key"/>) that was not among the keys the list showed last
/// time. Only on the same date (<see cref="ContextProperty"/>): moving to another day replaces
/// every row, and that is M5's to show, not a dozen arrivals.
/// </para>
/// <para>
/// A list that is out of sight (the day screen behind the editor on a phone) builds no rows, so
/// the comparison waits too: the row made in the editor arrives when the day comes back into view.
/// </para>
/// </remarks>
public static class RowArrivals
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<ItemsControl, bool>("IsEnabled", typeof(RowArrivals));

    /// <summary>What the rows belong to - the selected date. A change means a new list, not new rows.</summary>
    public static readonly AttachedProperty<object?> ContextProperty =
        AvaloniaProperty.RegisterAttached<ItemsControl, object?>("Context", typeof(RowArrivals));

    private sealed class Seen
    {
        public HashSet<string>? Keys;
        public object? Context;
        public bool CommitQueued;
    }

    private static readonly ConditionalWeakTable<ItemsControl, Seen> State = [];

    static RowArrivals()
    {
        IsEnabledProperty.Changed.AddClassHandler<ItemsControl>((list, e) =>
        {
            if (e.NewValue is true)
            {
                list.ContainerPrepared += OnContainerPrepared;
            }
            else
            {
                list.ContainerPrepared -= OnContainerPrepared;
            }
        });
    }

    public static bool GetIsEnabled(ItemsControl list) => list.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(ItemsControl list, bool value) => list.SetValue(IsEnabledProperty, value);

    public static object? GetContext(ItemsControl list) => list.GetValue(ContextProperty);

    public static void SetContext(ItemsControl list, object? value) => list.SetValue(ContextProperty, value);

    /// <summary>The key a row is known by, for the item types the lists show.</summary>
    internal static string? KeyOf(object? item) => item switch
    {
        TodoRowViewModel row => row.Item.Key,
        TodoItemViewModel todo => todo.Key,
        _ => null,
    };

    private static void OnContainerPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        if (sender is not ItemsControl list || KeyOf(list.ItemFromContainer(e.Container)) is not { } key)
        {
            return;
        }

        Seen seen = State.GetOrCreateValue(list);
        bool arrival = seen.Keys is { } known && Equals(seen.Context, GetContext(list)) && !known.Contains(key);
        QueueCommit(list, seen);

        if (!arrival)
        {
            return;
        }

        // Hidden until it plays, so the frame before the storyboard starts does not show the row
        // already in place.
        Control container = e.Container;
        container.Opacity = 0;
        Dispatcher.UIThread.Post(() => Arrive(container), DispatcherPriority.Loaded);
    }

    private static void Arrive(Control container)
    {
        container.Opacity = 1;
        if (container.GetVisualDescendants().OfType<Control>().FirstOrDefault(static c => c.Classes.Contains("rowcontent")) is not { } row ||
            container.GetVisualDescendants().OfType<Border>().FirstOrDefault(static b => b.Classes.Contains("rowflash")) is not { } flash)
        {
            return;
        }

        _ = MotionPlayer.Play(container, "arrive", Choreography.RowArrive(container, row, flash, container.Bounds.Height));
    }

    /// <summary>Once the rebuild has laid out, what it shows is what the next one is compared to.</summary>
    private static void QueueCommit(ItemsControl list, Seen seen)
    {
        if (seen.CommitQueued)
        {
            return;
        }

        seen.CommitQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            seen.CommitQueued = false;
            seen.Keys = [.. list.Items.Select(KeyOf).OfType<string>()];
            seen.Context = GetContext(list);
        }, DispatcherPriority.Background);
    }
}
