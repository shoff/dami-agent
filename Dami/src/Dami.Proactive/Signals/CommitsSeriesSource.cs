using Dami.Contracts.Domains;
using Dami.Proactive.CodeAudit;
using Microsoft.Extensions.Options;

namespace Dami.Proactive.Signals;

/// <summary>The repository's commits as a daily series.</summary>
public sealed class CommitsSeriesSource : IDailySeriesSource
{
    private readonly IGitLog gitLog;
    private readonly SignalsOptions signalsOptions;

    /// <summary>Creates the source.</summary>
    public CommitsSeriesSource(IGitLog gitLog, IOptions<SignalsOptions> signalsOptions)
    {
        ArgumentNullException.ThrowIfNull(gitLog);
        ArgumentNullException.ThrowIfNull(signalsOptions);
        this.gitLog = gitLog;
        this.signalsOptions = signalsOptions.Value;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DailySeries>> ReadAsync(
        DateOnly from, DateOnly to, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(zone);

        // A day earlier than the window's local midnight, so no zone arithmetic can lose the first day.
        var since = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(-1);
        var times = await this.gitLog.CommitTimesAsync(this.signalsOptions.RepoPath, since, cancellationToken)
            .ConfigureAwait(false);

        var byDay = new SortedDictionary<DateOnly, double>();
        foreach (var time in times)
        {
            var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time, zone).Date);
            if (day >= from && day <= to)
            {
                byDay[day] = byDay.GetValueOrDefault(day) + 1;
            }
        }

        var points = new List<DailyPoint>(byDay.Count);
        foreach (var (day, value) in byDay)
        {
            points.Add(new DailyPoint(day, value));
        }

        return [new DailySeries("commits", "commits to dami-agent", "commits", points)];
    }
}
