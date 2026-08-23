using Dami.Contracts.Proactive;
using Dami.Persistence.Proactive;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dami.Persistence.Tests.Proactive;

/// <summary>The surfacing queue, including the D-021 cap, against a live database.</summary>
[Collection(DatabaseCollection.NAME)]
public sealed class PostgresSurfacingQueueTests
{
    private static readonly DateTimeOffset createdAt = new(2026, 8, 22, 9, 0, 0, TimeSpan.Zero);

    private readonly DatabaseFixture fixture;

    public PostgresSurfacingQueueTests(DatabaseFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        this.fixture = fixture;
    }

    [Fact]
    public void Constructor_Should_Reject_A_Null_DataSource()
    {
        Assert.Throws<ArgumentNullException>(() => new PostgresSurfacingQueue(
            null!,
            Options.Create(new PostgresOptions()),
            Options.Create(new ProactiveOptions()),
            NullLogger<PostgresSurfacingQueue>.Instance));
    }

    [Fact]
    public async Task EnqueueAsync_Should_Accept_A_Surfacing_Under_The_Cap()
    {
        await this.fixture.ResetAsync();
        var queue = this.CreateQueue(cap: 3);

        var accepted = await queue.EnqueueAsync(Worth("the fuselage decals arrived"), CancellationToken.None);

        Assert.True(accepted);
    }

    [Fact]
    public async Task EnqueueAsync_Should_Make_The_Surfacing_Pending()
    {
        await this.fixture.ResetAsync();
        var queue = this.CreateQueue(cap: 3);
        await queue.EnqueueAsync(Worth("a talk on pgvector internals"), CancellationToken.None);

        Assert.Single(await this.PendingAsync(queue));
    }

    [Fact]
    public async Task EnqueueAsync_Should_Suppress_Beyond_The_Cap()
    {
        await this.fixture.ResetAsync();
        var queue = this.CreateQueue(cap: 2);
        await queue.EnqueueAsync(Worth("one"), CancellationToken.None);
        await queue.EnqueueAsync(Worth("two"), CancellationToken.None);

        var accepted = await queue.EnqueueAsync(Worth("three is too many"), CancellationToken.None);

        Assert.False(accepted);
    }

    [Fact]
    public async Task EnqueueAsync_Should_Keep_A_Suppressed_Surfacing_Out_Of_The_Queue()
    {
        await this.fixture.ResetAsync();
        var queue = this.CreateQueue(cap: 1);
        await queue.EnqueueAsync(Worth("the one allowed"), CancellationToken.None);
        await queue.EnqueueAsync(Worth("the suppressed"), CancellationToken.None);

        Assert.Single(await this.PendingAsync(queue));
    }

    [Fact]
    public async Task EnqueueAsync_Should_Count_The_Cap_Per_Service()
    {
        await this.fixture.ResetAsync();
        var queue = this.CreateQueue(cap: 1);
        await queue.EnqueueAsync(Worth("scout item"), CancellationToken.None);

        var other = new Surfacing(Guid.NewGuid(), "reflection", "weekly note", "body", 0.9, createdAt);
        var accepted = await queue.EnqueueAsync(other, CancellationToken.None);

        Assert.True(accepted);
    }

    [Fact]
    public async Task EnqueueAsync_Should_Only_Count_The_Rolling_Day_Against_The_Cap()
    {
        await this.fixture.ResetAsync();
        var queue = this.CreateQueue(cap: 1);
        var yesterday = new Surfacing(Guid.NewGuid(), "scout", "old", "body", 0.9, createdAt.AddDays(-2));
        await queue.EnqueueAsync(yesterday, CancellationToken.None);

        var accepted = await queue.EnqueueAsync(Worth("today"), CancellationToken.None);

        Assert.True(accepted);
    }

    [Fact]
    public async Task DeliverAsync_Should_Remove_The_Surfacing_From_Pending()
    {
        await this.fixture.ResetAsync();
        var queue = this.CreateQueue(cap: 3);
        var surfacing = Worth("something good");
        await queue.EnqueueAsync(surfacing, CancellationToken.None);

        await queue.DeliverAsync(surfacing.SurfacingId, createdAt.AddHours(1), CancellationToken.None);

        Assert.Empty(await this.PendingAsync(queue));
    }

    [Fact]
    public async Task RecordFeedbackAsync_Should_Persist_The_Reaction()
    {
        await this.fixture.ResetAsync();
        var queue = this.CreateQueue(cap: 3);
        var surfacing = Worth("a recommendation");
        await queue.EnqueueAsync(surfacing, CancellationToken.None);
        await queue.DeliverAsync(surfacing.SurfacingId, createdAt.AddHours(1), CancellationToken.None);

        await queue.RecordFeedbackAsync(surfacing.SurfacingId, "good call", createdAt.AddHours(2), CancellationToken.None);

        var feedback = await this.ReadFeedbackAsync(surfacing.SurfacingId);
        Assert.Equal("good call", feedback);
    }

    [Fact]
    public async Task PendingAsync_Should_Return_Oldest_First()
    {
        await this.fixture.ResetAsync();
        var queue = this.CreateQueue(cap: 5);
        var later = new Surfacing(Guid.NewGuid(), "scout", "later", "body", 0.9, createdAt.AddHours(2));
        await queue.EnqueueAsync(later, CancellationToken.None);
        await queue.EnqueueAsync(Worth("earlier"), CancellationToken.None);

        var pending = await this.PendingAsync(queue);

        Assert.Equal("earlier", pending[0].Title);
    }

    [Fact]
    public void PendingAsync_Should_Reject_A_Non_Positive_Limit()
    {
        var queue = this.CreateQueue(cap: 3);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => queue.PendingAsync(0, CancellationToken.None));
    }

    private static Surfacing Worth(string title)
    {
        return new Surfacing(Guid.NewGuid(), "scout", title, "the full text", 0.85, createdAt);
    }

    private async Task<List<Surfacing>> PendingAsync(ISurfacingQueue queue)
    {
        var pending = new List<Surfacing>();
        await foreach (var item in queue.PendingAsync(10, CancellationToken.None))
        {
            pending.Add(item);
        }

        return pending;
    }

    private async Task<string?> ReadFeedbackAsync(Guid surfacingId)
    {
        await using var command = this.fixture.DataSource.CreateCommand(
            $"select feedback from {DatabaseFixture.SCHEMA}.surfacings where surfacing_id = @id");
        command.Parameters.AddWithValue("id", surfacingId);
        return await command.ExecuteScalarAsync(CancellationToken.None) as string;
    }

    private PostgresSurfacingQueue CreateQueue(int cap)
    {
        return new PostgresSurfacingQueue(
            this.fixture.DataSource,
            Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }),
            Options.Create(new ProactiveOptions { MaxSurfacingsPerServicePerDay = cap }),
            NullLogger<PostgresSurfacingQueue>.Instance);
    }
}
