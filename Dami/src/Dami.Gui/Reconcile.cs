using System.Collections.ObjectModel;

namespace Dami.Gui;

/// <summary>Updates a bound collection in place instead of rebuilding it.</summary>
/// <remarks>
/// Every panel in this window re-polls on a timer — the sidebars every two seconds, the
/// board every five — and almost always receives exactly what it is already showing. The
/// original code cleared the collection and re-added, which raises
/// <see cref="System.Collections.Specialized.NotifyCollectionChangedAction.Reset"/>: the
/// items control tears down and rebuilds every container, and the surrounding
/// <c>ScrollViewer</c> drops back to offset zero. Visibly that is a list flickering twice
/// a second that cannot be scrolled, because every scroll is undone before the pointer
/// moves. Nothing here is cosmetic — a Reset on a timer makes a panel unusable.
///
/// So: compare first, mutate only what differs, and never Reset. Element equality does
/// the work, which is why the bound item types carry value equality rather than the
/// reference equality freshly-polled objects would otherwise have.
/// </remarks>
public static class Reconcile
{
    /// <summary>
    /// Makes <paramref name="target"/> match <paramref name="desired"/> with the fewest
    /// notifications, and reports whether anything actually moved.
    /// </summary>
    public static bool Sync<T>(ObservableCollection<T> target, IReadOnlyList<T> desired)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(desired);

        var changed = Overwrite(target, desired);
        return Trim(target, desired.Count) || changed;
    }

    /// <summary>Replaces differing positions and appends anything past the end.</summary>
    private static bool Overwrite<T>(ObservableCollection<T> target, IReadOnlyList<T> desired)
    {
        var comparer = EqualityComparer<T>.Default;
        var changed = false;

        for (var index = 0; index < desired.Count; index++)
        {
            if (index >= target.Count)
            {
                target.Add(desired[index]);
                changed = true;
            }
            else if (!comparer.Equals(target[index], desired[index]))
            {
                target[index] = desired[index];
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    /// Drops the surplus tail. Removing from the end never shifts a surviving index, so
    /// the view keeps the containers it already built.
    /// </summary>
    private static bool Trim<T>(ObservableCollection<T> target, int keep)
    {
        var changed = false;
        while (target.Count > keep)
        {
            target.RemoveAt(target.Count - 1);
            changed = true;
        }

        return changed;
    }
}
