using Dami.Contracts.Calendar;
using Dami.Contracts.Domains;
using Dami.Contracts.Finance;
using Dami.Contracts.Memory;
using Dami.Contracts.Models;
using Dami.Contracts.Proactive;
using Dami.Proactive.Vault;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace Dami.Proactive.Tests.Vault;

/// <summary>The nightly vault: memory mirrored as Markdown, and a diary of the day before (B6, B4).</summary>
public sealed class VaultServicesTests : IDisposable
{
    // 2026-09-30 03:00 CDT: "yesterday" is Tuesday 2026-09-29.
    private static readonly DateTimeOffset now = new(2026, 9, 30, 8, 0, 0, TimeSpan.Zero);

    private readonly string directory = Directory.CreateTempSubdirectory("dami-vault-").FullName;
    private readonly IConclusionLedger beliefs = Substitute.For<IConclusionLedger>();
    private readonly IObservationCorpus corpus = Substitute.For<IObservationCorpus>();
    private readonly IExpenseLedger expenses = Substitute.For<IExpenseLedger>();
    private readonly ICalendarStore calendar = Substitute.For<ICalendarStore>();
    private readonly IDomainFactStore facts = Substitute.For<IDomainFactStore>();
    private readonly IChatClient local = Substitute.For<IChatClient>();

    public VaultServicesTests()
    {
        this.beliefs.ActiveAsOfAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(_ => ManyAsync(
            new Conclusion(Guid.NewGuid(), null, "work", "Ships vertical slices", 0.8, ConclusionSource.ReflectionPass, now.AddDays(-9))));
        this.corpus.FromSourceAsync("lesson", Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(_ => ManyAsync(
            new Observation(Guid.NewGuid(), now.AddDays(-1), "lesson", "Be brief.")));
        this.expenses.BetweenAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(
            [new Expense(Guid.NewGuid(), new DateOnly(2026, 9, 29), "Aldi", 31.07m, "USD", "groceries", "mail", now)]);
        this.calendar.BetweenAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([new CalendarEvent("d@1", now.AddHours(-18), null, false, "Dentist", null)]);
        this.facts.BetweenAsync("mail", Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => ManyAsync<DomainFact>());
        this.corpus.BetweenAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(_ => ManyAsync(
            new Observation(Guid.NewGuid(), now.AddHours(-20), "chat", "Steve asked: should I refactor the gate? — Dami answered: split the batch")));
        this.local.CompleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns("## Decisions\n- Split the gate batch.\n## Open loops\n- none");
    }

    public void Dispose() => Directory.Delete(this.directory, true);

    private static async IAsyncEnumerable<T> ManyAsync<T>(params T[] items)
    {
        foreach (var item in items)
        {
            yield return item;
        }

        await Task.CompletedTask;
    }

    private IOptions<VaultOptions> Options() => Microsoft.Extensions.Options.Options.Create(new VaultOptions { Directory = this.directory });

    private static ProactiveContext Context() => new(Guid.NewGuid(), now, null);

    [Fact]
    public async Task The_Export_Should_Write_Beliefs_Lessons_And_This_Months_Expenses()
    {
        var service = new VaultExportService(
            this.beliefs, this.corpus, this.expenses, new FileVault(this.Options()), new FakeTimeProvider(now));

        await service.RunPassAsync(Context(), CancellationToken.None);

        Assert.Contains("Ships vertical slices", await File.ReadAllTextAsync(Path.Combine(this.directory, "Beliefs.md")));
        Assert.Contains("Be brief.", await File.ReadAllTextAsync(Path.Combine(this.directory, "Lessons.md")));
        Assert.Contains("Aldi", await File.ReadAllTextAsync(Path.Combine(this.directory, "Expenses", "2026-09.md")));
    }

    [Fact]
    public async Task The_Diary_Should_Write_Yesterdays_Entry_And_Keep_It_In_Memory_Once()
    {
        var service = this.Diary();

        await service.RunPassAsync(Context(), CancellationToken.None);
        await service.RunPassAsync(Context(), CancellationToken.None);

        var entry = await File.ReadAllTextAsync(Path.Combine(this.directory, "Journal", "2026-09-29.md"));
        Assert.StartsWith("# Tuesday, September 29 2026", entry, StringComparison.Ordinal);
        Assert.Contains("Split the gate batch.", entry, StringComparison.Ordinal);
        await this.local.Received().CompleteAsync(
            Arg.Is<string>(prompt => prompt.Contains("should I refactor the gate", StringComparison.Ordinal)
                && prompt.Contains("Dentist", StringComparison.Ordinal)
                && prompt.Contains("Aldi", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
        var recorded = this.corpus.ReceivedCalls().Where(call => call.GetMethodInfo().Name == "RecordAsync")
            .Select(call => ((Observation)call.GetArguments()[0]!).ObservationId).Distinct().ToList();
        Assert.Single(recorded);
    }

    [Fact]
    public async Task A_Quiet_Day_Should_Not_Get_A_Diary()
    {
        this.corpus.BetweenAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(_ => ManyAsync<Observation>());
        this.expenses.BetweenAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([]);
        this.calendar.BetweenAsync(Arg.Any<DateTimeOffset>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([]);

        await this.Diary().RunPassAsync(Context(), CancellationToken.None);

        await this.local.DidNotReceiveWithAnyArgs().CompleteAsync(default!, default);
        Assert.False(Directory.Exists(Path.Combine(this.directory, "Journal")));
    }

    [Fact]
    public async Task The_Vault_Should_Refuse_A_Path_Outside_Itself()
    {
        await Assert.ThrowsAsync<ArgumentException>(
            () => new FileVault(this.Options()).WriteAsync("../escape.md", "x", CancellationToken.None));
    }

    private DailyDiaryService Diary() => new(
        this.corpus, this.expenses, this.calendar, this.facts, this.local, new FileVault(this.Options()), this.Options(),
        new FakeTimeProvider(now), NullLogger<DailyDiaryService>.Instance);
}
