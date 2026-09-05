using Dami.Contracts.Scheduling;
using Dami.Persistence.Scheduling;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dami.Persistence.Tests.Scheduling;

/// <summary>The job store against a live database, delivery column included (migration 039).</summary>
[Collection(DatabaseCollection.NAME)]
public sealed class PostgresScheduledJobStoreTests
{
    private static readonly DateTimeOffset at = new(2026, 9, 4, 22, 0, 0, TimeSpan.Zero);

    private readonly DatabaseFixture fixture;

    public PostgresScheduledJobStoreTests(DatabaseFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        this.fixture = fixture;
    }

    private static ScheduledJob Job(string? delivery) => new(
        Guid.NewGuid(), "morning portrait", "a picture each morning", ScheduledJobKind.Prompt,
        "make a picture of yourself starting the day", [], "0 7 * * *", "America/Chicago",
        ScheduledJobStatus.Draft, at, null, null, null, null, delivery);

    private PostgresScheduledJobStore Store() => new(
        this.fixture.DataSource, Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }));

    [Fact]
    public async Task A_Job_Should_Round_Trip_Its_Delivery()
    {
        await this.fixture.ResetAsync();
        var store = this.Store();
        var job = Job("discord:1543678906748641310");

        await store.AddAsync(job, CancellationToken.None);
        var found = await store.FindAsync(job.JobId, CancellationToken.None);

        Assert.Equal("discord:1543678906748641310", found!.Delivery);
        Assert.Equal(job.Payload, found.Payload);
    }

    [Fact]
    public async Task A_Job_Without_Delivery_Should_Read_Back_Null()
    {
        await this.fixture.ResetAsync();
        var store = this.Store();
        var job = Job(null);

        await store.AddAsync(job, CancellationToken.None);

        Assert.Null((await store.FindAsync(job.JobId, CancellationToken.None))!.Delivery);
    }

    [Fact]
    public async Task Update_Should_Keep_The_Delivery()
    {
        await this.fixture.ResetAsync();
        var store = this.Store();
        var job = Job("gui");
        await store.AddAsync(job, CancellationToken.None);

        await store.UpdateAsync(job with { Status = ScheduledJobStatus.Active, NextRunAt = at.AddDays(1) }, CancellationToken.None);

        var listed = Assert.Single(await store.ListAsync(CancellationToken.None));
        Assert.Equal(("gui", ScheduledJobStatus.Active), (listed.Delivery, listed.Status));
    }
}
