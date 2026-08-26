using Dami.Contracts.Domains;
using Dami.Persistence.Domains;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dami.Persistence.Tests.Domains;

[Collection(DatabaseCollection.NAME)]
public sealed class PostgresDomainFactStoreTests
{
    private static readonly DateTimeOffset at = new(2026, 8, 25, 22, 0, 0, TimeSpan.Zero);
    private readonly DatabaseFixture fixture;

    public PostgresDomainFactStoreTests(DatabaseFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task Store_Should_Record_Once_Per_Day_List_Newest_First_And_Hide_Rejected_Facts()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        var today = new DateOnly(2026, 8, 25);
        var first = await store.RecordAsync(Fact("network", today, "gateway", "Default gateway is 192.168.4.1"), CancellationToken.None);
        var same = await store.RecordAsync(Fact("network", today, "gateway", "Default gateway is 192.168.4.1"), CancellationToken.None);
        var nextDay = await store.RecordAsync(Fact("network", today.AddDays(1), "gateway", "Default gateway is 192.168.4.1"), CancellationToken.None);
        var other = await store.RecordAsync(Fact("civic", today, "meeting", "Council meets Monday"), CancellationToken.None);

        var network = await ListAsync(store.TimelineAsync("network", 10, CancellationToken.None));
        var rejected = await store.RejectAsync(network[1].FactId, "wrong", CancellationToken.None);
        var unknown = await store.RejectAsync(Guid.NewGuid(), "nothing", CancellationToken.None);
        var afterReject = await ListAsync(store.TimelineAsync("network", 10, CancellationToken.None));
        var all = await ListAsync(store.TimelineAsync(null, 10, CancellationToken.None));
        var domains = await store.DomainsAsync(CancellationToken.None);

        Assert.True(first);
        Assert.False(same);
        Assert.True(nextDay);
        Assert.True(other);
        Assert.Equal([today.AddDays(1), today], network.Select(fact => fact.AsOf));
        Assert.True(rejected);
        Assert.False(unknown);
        Assert.Single(afterReject);
        Assert.Equal(2, all.Count);
        Assert.Equal([("civic", 1), ("network", 1)], domains);
    }

    [Fact]
    public async Task DomainFactSource_Should_Serve_One_Domain_As_Structured_Facts()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        await store.RecordAsync(Fact("network", new DateOnly(2026, 8, 25), "service", "ollama on 127.0.0.1:11434 is listening"), CancellationToken.None);
        await store.RecordAsync(Fact("civic", new DateOnly(2026, 8, 25), "meeting", "Council meets Monday"), CancellationToken.None);
        var source = new DomainFactSource(store, "network");

        var facts = new List<Dami.Contracts.Context.StructuredFact>();
        await foreach (var fact in source.RelevantAsync("is ollama up?", 5, CancellationToken.None))
        {
            facts.Add(fact);
        }

        Assert.Equal("network", source.Domain);
        var only = Assert.Single(facts);
        Assert.Equal(("ollama on 127.0.0.1:11434 is listening", "service"), (only.Text, only.Kind));
    }

    [Fact]
    public async Task BetweenAsync_Should_Serve_A_Window_Soonest_First()
    {
        await this.fixture.ResetAsync();
        var store = this.CreateStore();
        await store.RecordAsync(Fact("civic", new DateOnly(2026, 8, 31), "meeting", "Council"), CancellationToken.None);
        await store.RecordAsync(Fact("civic", new DateOnly(2026, 8, 26), "meeting", "Finance"), CancellationToken.None);
        await store.RecordAsync(Fact("civic", new DateOnly(2026, 9, 9), "meeting", "Too late"), CancellationToken.None);

        var window = await ListAsync(store.BetweenAsync("civic", new DateOnly(2026, 8, 25), new DateOnly(2026, 9, 1), 10, CancellationToken.None));

        Assert.Equal(["Finance", "Council"], window.Select(fact => fact.Description));
    }

    private static DomainFact Fact(string domain, DateOnly asOf, string category, string description)
    {
        return new DomainFact(Guid.NewGuid(), domain, asOf, category, description, "test", at);
    }

    private static async Task<List<DomainFact>> ListAsync(IAsyncEnumerable<DomainFact> facts)
    {
        var list = new List<DomainFact>();
        await foreach (var fact in facts)
        {
            list.Add(fact);
        }

        return list;
    }

    private PostgresDomainFactStore CreateStore()
    {
        return new PostgresDomainFactStore(
            this.fixture.DataSource, Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }));
    }
}
