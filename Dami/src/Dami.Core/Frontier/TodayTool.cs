using System.Globalization;
using System.Text.Json;
using Dami.Contracts.Domains;
using Dami.Contracts.Memory;
using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
using Dami.Contracts.Proactive;
using Dami.Contracts.Scheduling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Core.Frontier;

/// <summary>Lets the frontier assemble a morning: the day, what was noticed, the gym, what is scheduled, a year ago.</summary>
public interface IFrontierToday
{
    /// <summary>The tool as offered to the frontier.</summary>
    FrontierTool Tool { get; }

    /// <summary>Everything this host knows about today, gated.</summary>
    Task<FrontierToolResult> TodayAsync(Guid traceId, CancellationToken cancellationToken);
}

/// <summary>
/// One tool that reads every local source the brief needs and hands the frontier gated
/// lines to write from. It holds the profile stores and nothing that can leave; the
/// lines pass the disclosure gate exactly as retrieved context does.
/// </summary>
public sealed class TodayTool : IFrontierToday
{
    /// <summary>The tool's name on the wire.</summary>
    public const string NAME = "today";

    private const int NOTICED = 5;
    private const int YEAR_AGO = 3;
    private const int WEATHER = 4;

    private readonly IFitnessStore fitness;
    private readonly IDomainFactStore facts;
    private readonly ISurfacingQueue surfacings;
    private readonly IScheduledJobStore jobs;
    private readonly IObservationCorpus corpus;
    private readonly IContextDisclosureGate gate;
    private readonly IDisclosureLedger ledger;
    private readonly DisclosureMemo memo;
    private readonly AugmentedTurnOptions turnOptions;
    private readonly TimeProvider clock;
    private readonly ILogger<TodayTool> logger;

    /// <summary>Creates the tool.</summary>
    public TodayTool(
        IFitnessStore fitness,
        IDomainFactStore facts,
        ISurfacingQueue surfacings,
        IScheduledJobStore jobs,
        IObservationCorpus corpus,
        IContextDisclosureGate gate,
        IDisclosureLedger ledger,
        DisclosureMemo memo,
        IOptions<AugmentedTurnOptions> turnOptions,
        TimeProvider clock,
        ILogger<TodayTool> logger)
    {
        ArgumentNullException.ThrowIfNull(fitness);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(surfacings);
        ArgumentNullException.ThrowIfNull(jobs);
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(gate);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(memo);
        ArgumentNullException.ThrowIfNull(turnOptions);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        this.fitness = fitness;
        this.facts = facts;
        this.surfacings = surfacings;
        this.jobs = jobs;
        this.corpus = corpus;
        this.gate = gate;
        this.ledger = ledger;
        this.memo = memo;
        this.turnOptions = turnOptions.Value;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public FrontierTool Tool { get; } = new(
        NAME,
        "Everything Dami knows about today, for a morning briefing or a 'what's on today' — "
        + "the date and time, the weather on record, the last week in the gym and anything the "
        + "log noticed, what she has noticed and not yet mentioned, what is scheduled, and what "
        + "was happening a year ago today. Use it when Steve asks for his briefing, his morning, "
        + "or what is going on; write the brief in your own voice from these lines, short.",
        JsonSerializer.SerializeToElement(new { type = "object", properties = new { }, additionalProperties = false }));

    /// <inheritdoc />
    public async Task<FrontierToolResult> TodayAsync(Guid traceId, CancellationToken cancellationToken)
    {
        var now = this.clock.GetUtcNow();
        var local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local);
        var lines = new List<string> { $"Now: {local.ToString("dddd, MMMM d yyyy, h:mm tt", CultureInfo.InvariantCulture)} ({TimeZoneInfo.Local.StandardName})" };
        lines.AddRange(await this.WeatherAsync(local, cancellationToken).ConfigureAwait(false));
        lines.AddRange(await this.GymAsync(now, cancellationToken).ConfigureAwait(false));
        lines.AddRange(await this.NoticedAsync(cancellationToken).ConfigureAwait(false));
        lines.AddRange(await this.ScheduledAsync(now, cancellationToken).ConfigureAwait(false));
        lines.AddRange(await this.YearAgoAsync(now, cancellationToken).ConfigureAwait(false));

        var decided = await this.DecideAsync(traceId, lines, cancellationToken).ConfigureAwait(false);
        var sendable = decided.Where(item => item.Disclosure != Disclosure.Withhold).Select(item => item.Sendable).ToList();
        this.logger.LogInformation("today: {Lines} line(s), {Sent} sent", lines.Count, sendable.Count);
        return FrontierToolResult.Ok(string.Join('\n', sendable));
    }

    private async Task<List<string>> WeatherAsync(DateTimeOffset local, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(local.DateTime);
        var lines = new List<string>();
        await foreach (var fact in this.facts.BetweenAsync("weather", today, today, WEATHER, cancellationToken).ConfigureAwait(false))
        {
            lines.Add("Weather: " + fact.Description);
        }

        return lines;
    }

    private async Task<List<string>> GymAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var snapshot = await this.fitness.SnapshotAsync(cancellationToken).ConfigureAwait(false);
        var lastLift = snapshot.Sets.OrderByDescending(set => set.OccurredAt).FirstOrDefault();
        var lines = new List<string>();
        if (lastLift is not null)
        {
            lines.Add($"Gym: last lifting session {lastLift.OccurredAt:MMM d} ({(now - lastLift.OccurredAt).Days} days ago).");
        }

        lines.AddRange(FitnessInsights.Analyze(snapshot, now).Select(insight => "Gym: " + insight.Text));
        return lines;
    }

    private async Task<List<string>> NoticedAsync(CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        await foreach (var surfacing in this.surfacings.PendingAsync(NOTICED, cancellationToken).ConfigureAwait(false))
        {
            lines.Add($"Noticed, not yet mentioned: {surfacing.Title} — {surfacing.Body.ReplaceLineEndings(" ")}");
        }

        return lines;
    }

    private async Task<List<string>> ScheduledAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var due = (await this.jobs.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(job => job.Status == ScheduledJobStatus.Active && job.NextRunAt is { } next && next <= now.AddHours(24))
            .OrderBy(job => job.NextRunAt);
        return due.Select(job =>
            $"Scheduled: '{job.Name}' at {TimeZoneInfo.ConvertTime(job.NextRunAt!.Value, TimeZoneInfo.Local):h:mm tt}").ToList();
    }

    private async Task<List<string>> YearAgoAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var day = now.AddYears(-1).Date;
        var lines = new List<string>();
        await foreach (var observation in this.corpus.BetweenAsync(day, day.AddDays(1), cancellationToken).ConfigureAwait(false))
        {
            if (lines.Count >= YEAR_AGO)
            {
                break;
            }

            lines.Add("A year ago today: " + observation.Body.ReplaceLineEndings(" "));
        }

        return lines;
    }

    private Task<IReadOnlyList<DisclosedItem>> DecideAsync(Guid traceId, List<string> lines, CancellationToken cancellationToken)
    {
        if (!this.turnOptions.Gate)
        {
            return Task.FromResult<IReadOnlyList<DisclosedItem>>([.. lines.Select(line => new DisclosedItem(line, Disclosure.Pass, line, "gate disabled"))]);
        }

        return this.memo.DecideAsync(lines, TimeSpan.FromMinutes(this.turnOptions.GateMemoMinutes), async fresh =>
        {
            var decided = await this.gate.ClassifyAsync("the morning briefing", fresh, cancellationToken).ConfigureAwait(false);
            await this.ledger.RecordAsync(traceId, NAME, decided, this.clock.GetUtcNow(), cancellationToken).ConfigureAwait(false);
            return decided;
        }, cancellationToken);
    }
}
