using Dami.Contracts.Domains;
using Dami.Contracts.Memory;
using Dami.Contracts.Privacy;
using Dami.Contracts.Proactive;
using Dami.Contracts.Scheduling;
using Dami.Core.Frontier;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Dami.Core.Tests.Frontier;

public sealed class TodayToolTests
{
    private readonly IFitnessStore fitness = Substitute.For<IFitnessStore>();
    private readonly IDomainFactStore facts = Substitute.For<IDomainFactStore>();
    private readonly ISurfacingQueue surfacings = Substitute.For<ISurfacingQueue>();
    private readonly IScheduledJobStore jobs = Substitute.For<IScheduledJobStore>();
    private readonly IObservationCorpus corpus = Substitute.For<IObservationCorpus>();
    private readonly IContextDisclosureGate gate = Substitute.For<IContextDisclosureGate>();
    private readonly Dami.Contracts.Calendar.ICalendarStore calendar = Substitute.For<Dami.Contracts.Calendar.ICalendarStore>();

    private static async IAsyncEnumerable<T> ManyAsync<T>(params T[] items)
    {
        foreach (var item in items)
        {
            yield return item;
        }

        await Task.CompletedTask;
    }

    private static readonly DateTimeOffset now = new(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    private TodayTool Subject(bool gated)
    {
        this.fitness.SnapshotAsync(Arg.Any<CancellationToken>()).Returns(new FitnessSnapshot([], [
            new FitnessSet(Guid.NewGuid(), Guid.NewGuid(), now.AddDays(-1), "row", "back", 1, 10, 100m, 7, false)], []));
        this.facts.BetweenAsync("weather", Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ManyAsync(new DomainFact(Guid.NewGuid(), "weather", DateOnly.FromDateTime(now.DateTime), "forecast", "high 72, clear", "nws", now)));
        this.surfacings.PendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ManyAsync(new Surfacing(Guid.NewGuid(), "fitness-review", "Your week in the gym", "3 sessions", 0.7, now)));
        this.jobs.ListAsync(Arg.Any<CancellationToken>()).Returns([
            new ScheduledJob(Guid.NewGuid(), "morning portrait", "d", ScheduledJobKind.Prompt, "p", [], "0 7 * * *", "UTC",
                ScheduledJobStatus.Active, now, now, now.AddHours(2), null, null, "discord:1"),
            new ScheduledJob(Guid.NewGuid(), "far off", "d", ScheduledJobKind.Prompt, "p", [], "0 7 * * *", "UTC",
                ScheduledJobStatus.Active, now, now, now.AddDays(3), null, null, null)]);
        this.corpus.BetweenAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(ManyAsync(new Observation(Guid.NewGuid(), now.AddYears(-1), "hermes", "Started the Spitfire build.")));
        this.gate.ClassifyAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<IReadOnlyList<string>>(1)
                .Select(line => new DisclosedItem(line, line.StartsWith("A year ago", StringComparison.Ordinal) ? Disclosure.Withhold : Disclosure.Pass, line, "t")).ToList());
        this.calendar.BetweenAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([
                new Dami.Contracts.Calendar.CalendarEvent("b@1", now.AddHours(-6), null, true, "Mom's birthday", null),
                new Dami.Contracts.Calendar.CalendarEvent("d@1", now.AddHours(2), now.AddHours(2.5), false, "Dentist", "Main St Dental"),
            ]);
        return new TodayTool(
            this.fitness, this.facts, this.surfacings, this.jobs, this.corpus, this.calendar, this.gate, Substitute.For<IDisclosureLedger>(),
            new DisclosureMemo(new FakeTimeProvider(now)), Options.Create(new AugmentedTurnOptions { Gate = gated, GateMemoMinutes = 0 }),
            new FakeTimeProvider(now), NullLogger<TodayTool>.Instance);
    }

    [Fact]
    public async Task Today_Should_Assemble_The_Day_From_Every_Local_Source()
    {
        var result = await this.Subject(gated: false).TodayAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.StartsWith("Now: ", result.Text, StringComparison.Ordinal);
        Assert.Contains("Weather: high 72, clear", result.Text, StringComparison.Ordinal);
        Assert.Contains("Gym: last lifting session", result.Text, StringComparison.Ordinal);
        Assert.Contains("Noticed, not yet mentioned: Your week in the gym", result.Text, StringComparison.Ordinal);
        Assert.Contains("Scheduled: 'morning portrait' at", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("far off", result.Text, StringComparison.Ordinal);
        Assert.Contains("A year ago today: Started the Spitfire build.", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Today_Should_Carry_Todays_Calendar_In_Local_Time()
    {
        // D1: calendar was the most-used integration in the 2026-09-29 survey.
        var result = await this.Subject(gated: false).TodayAsync(Guid.NewGuid(), CancellationToken.None);

        string At(double hours) => TimeZoneInfo.ConvertTime(now.AddHours(hours), TimeZoneInfo.Local)
            .ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture);
        var dentist = $"{At(2)}–{At(2.5)}";
        Assert.Contains("Calendar: all day — Mom's birthday", result.Text, StringComparison.Ordinal);
        Assert.Contains($"Calendar: {dentist} Dentist (Main St Dental)", result.Text, StringComparison.Ordinal);
        var local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local).Date;
        var midnight = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
        await this.calendar.Received(1).BetweenAsync(midnight, midnight.AddDays(1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Today_Should_Pass_Its_Lines_Through_The_Gate()
    {
        var result = await this.Subject(gated: true).TodayAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Contains("Weather: high 72, clear", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Spitfire", result.Text, StringComparison.Ordinal);
    }
}
