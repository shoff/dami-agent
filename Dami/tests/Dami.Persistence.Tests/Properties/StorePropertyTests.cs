using Dami.Contracts.Events;
using Dami.Contracts.Memory;
using Dami.Persistence.Events;
using Dami.Persistence.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dami.Persistence.Tests.Properties;

/// <summary>N5: randomized properties the stores must hold for ANY input, not just the
/// examples other tests pick. Seeds are fixed so a failure reproduces exactly.</summary>
[Collection(DatabaseCollection.NAME)]
public sealed class StorePropertyTests
{
    private const int ROUNDS = 25;
    private static readonly DateTimeOffset at = new(2026, 8, 24, 3, 0, 0, TimeSpan.Zero);

    private readonly DatabaseFixture fixture;

    public StorePropertyTests(DatabaseFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        this.fixture = fixture;
    }

    [Fact]
    public async Task Corpus_Should_Round_Trip_Any_Body_Byte_Exactly()
    {
        await this.fixture.ResetAsync();
        var corpus = this.CreateCorpus();
        var random = new Random(20260824);
        for (var round = 0; round < ROUNDS; round++)
        {
            var body = RandomText(random);
            var observation = new Observation(Guid.NewGuid(), at, "property-test", body);
            await corpus.RecordAsync(observation, CancellationToken.None);

            var found = await corpus.FindAsync(observation.ObservationId, CancellationToken.None);
            Assert.Equal(body, found!.Body);
        }
    }

    [Fact]
    public async Task EventStore_Should_Keep_One_Row_However_Often_An_Event_Repeats()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateEventStore();
        var random = new Random(20260825);
        var executionEvent = RandomEvent(random);
        var sequences = new HashSet<long>();
        for (var attempt = 0; attempt < 10; attempt++)
        {
            sequences.Add(await store.AppendAsync(executionEvent, CancellationToken.None));
        }

        Assert.Single(sequences);
    }

    [Fact]
    public async Task EventStore_Should_Replay_A_Trace_In_Persistence_Order()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateEventStore();
        var random = new Random(20260826);
        var traceId = Guid.NewGuid();
        var appended = new List<Guid>();
        for (var round = 0; round < ROUNDS; round++)
        {
            var executionEvent = RandomEvent(random, traceId);
            appended.Add(executionEvent.EventId);
            await store.AppendAsync(executionEvent, CancellationToken.None);
        }

        var replayed = new List<Guid>();
        await foreach (var item in store.ReplayAsync(traceId, CancellationToken.None))
        {
            replayed.Add(item.EventId);
        }

        Assert.Equal(appended, replayed);
    }

    [Fact]
    public async Task Ledger_AsOf_Should_Match_A_Manual_Reconstruction_For_Any_History()
    {
        await this.fixture.ResetAsync();
        var ledger = this.CreateLedger();
        var random = new Random(20260827);
        var history = await BuildRandomHistoryAsync(ledger, random);

        var probe = at.AddHours(random.Next(0, 72));
        var expected = history
            .Where(item => item.ConcludedAt <= probe
                && (item.RetractedAt is null || item.RetractedAt > probe))
            .Select(item => item.ConclusionId)
            .ToHashSet();

        var actual = new HashSet<Guid>();
        await foreach (var conclusion in ledger.ActiveAsOfAsync(probe, CancellationToken.None))
        {
            actual.Add(conclusion.ConclusionId);
        }

        Assert.Equal(expected, actual);
    }

    private static async Task<List<Conclusion>> BuildRandomHistoryAsync(
        PostgresConclusionLedger ledger,
        Random random)
    {
        var history = new List<Conclusion>();
        for (var round = 0; round < ROUNDS; round++)
        {
            var concludedAt = at.AddHours(random.Next(0, 48));
            var retractedAt = random.Next(3) == 0
                ? concludedAt.AddHours(random.Next(1, 24))
                : (DateTimeOffset?)null;
            var conclusion = new Conclusion(
                Guid.NewGuid(), null, "steve", $"random belief {round}", 0.8,
                ConclusionSource.ReflectionPass, concludedAt);
            await ledger.RecordAsync(conclusion, CancellationToken.None);
            if (retractedAt is not null)
            {
                await ledger.RetractAsync(
                    conclusion.ConclusionId, "property", retractedAt.Value, CancellationToken.None);
            }

            history.Add(new Conclusion(
                conclusion.ConclusionId, null, "steve", conclusion.Statement, 0.8,
                ConclusionSource.ReflectionPass, concludedAt, retractedAt: retractedAt,
                retractionReason: retractedAt is null ? null : "property"));
        }

        return history;
    }

    private static readonly string[] runes =
        ["é", "ü", "ñ", "→", "木", "水", "🔥", "𝄞", " ", "\t", "'", "\"", "\\"];

    private static string RandomText(Random random)
    {
        var pieces = random.Next(1, 200);
        var text = new System.Text.StringBuilder();
        for (var index = 0; index < pieces; index++)
        {
            if (random.Next(4) == 0)
            {
                text.Append(runes[random.Next(runes.Length)]);
            }
            else
            {
                text.Append((char)random.Next(' ', '~' + 1));
            }
        }

        return text.ToString();
    }

    private static ExecutionEvent RandomEvent(Random random, Guid? traceId = null)
    {
        return new ExecutionEvent(
            Guid.NewGuid(), traceId ?? Guid.NewGuid(), Guid.NewGuid(), null,
            ExecutionOrigin.UserTurn, "property-test",
            (ExecutionEventType)random.Next(23, 27), ExecutionStatus.Running,
            at.AddMinutes(random.Next(0, 1000)), RandomText(random));
    }

    private PostgresObservationCorpus CreateCorpus()
    {
        return new PostgresObservationCorpus(
            this.fixture.DataSource, this.Options_(), NullLogger<PostgresObservationCorpus>.Instance);
    }

    private PostgresExecutionEventStore CreateEventStore()
    {
        return new PostgresExecutionEventStore(
            this.fixture.DataSource, this.Options_(), NullLogger<PostgresExecutionEventStore>.Instance);
    }

    private PostgresConclusionLedger CreateLedger()
    {
        return new PostgresConclusionLedger(
            this.fixture.DataSource, this.Options_(), NullLogger<PostgresConclusionLedger>.Instance);
    }

    private IOptions<PostgresOptions> Options_()
    {
        return Microsoft.Extensions.Options.Options.Create(
            new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA });
    }
}
