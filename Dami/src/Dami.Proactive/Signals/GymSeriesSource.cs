using Dami.Contracts.Domains;

namespace Dami.Proactive.Signals;

/// <summary>The gym log as two daily series: working volume in pounds, and working sets.</summary>
public sealed class GymSeriesSource : IDailySeriesSource
{
    private readonly IFitnessStore store;

    /// <summary>Creates the source.</summary>
    public GymSeriesSource(IFitnessStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        this.store = store;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DailySeries>> ReadAsync(
        DateOnly from, DateOnly to, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var snapshot = await this.store.SnapshotAsync(cancellationToken).ConfigureAwait(false);
        var volume = new SortedDictionary<DateOnly, double>();
        var sets = new SortedDictionary<DateOnly, double>();
        foreach (var set in snapshot.Sets)
        {
            if (set.IsWarmup || set.WeightLbs is not { } weight || set.Reps is not { } reps || reps <= 0)
            {
                continue;
            }

            var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(set.OccurredAt, zone).Date);
            if (day < from || day > to)
            {
                continue;
            }

            volume[day] = volume.GetValueOrDefault(day) + ((double)weight * reps);
            sets[day] = sets.GetValueOrDefault(day) + 1;
        }

        return
        [
            new DailySeries("gym-volume", "gym volume", "lb", Points(volume)),
            new DailySeries("gym-sets", "working sets in the gym", "sets", Points(sets)),
        ];
    }

    private static List<DailyPoint> Points(SortedDictionary<DateOnly, double> byDay)
    {
        var points = new List<DailyPoint>(byDay.Count);
        foreach (var (day, value) in byDay)
        {
            points.Add(new DailyPoint(day, value));
        }

        return points;
    }
}
