using Dami.Contracts.Capabilities;
using Dami.Contracts.Events;
using Dami.Persistence.Events;
using Dami.Persistence.Skills;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace Dami.Persistence.Tests.Skills;

[Collection(DatabaseCollection.NAME)]
public sealed class PostgresSkillChangeStoreTests
{
    private static readonly DateTimeOffset at =
        new(2026, 8, 24, 20, 0, 0, TimeSpan.Zero);

    private readonly DatabaseFixture fixture;

    public PostgresSkillChangeStoreTests(DatabaseFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        this.fixture = fixture;
    }

    [Fact]
    public void Constructor_Should_Reject_A_Null_Data_Source()
    {
        Assert.Throws<ArgumentNullException>(() => new PostgresSkillChangeStore(
            null!, Options.Create(new PostgresOptions())));
    }

    [Fact]
    public void Constructor_Should_Reject_Null_Options()
    {
        Assert.Throws<ArgumentNullException>(() => new PostgresSkillChangeStore(
            this.fixture.DataSource, null!));
    }

    [Fact]
    public async Task CreateAsync_Should_Atomically_Persist_The_Diff_And_Requested_Event()
    {
        await this.fixture.ResetAsync();
        var record = CreateRecord();
        ISkillChangeStore store = this.CreateStore();

        await store.CreateAsync(record, CancellationToken.None);

        SkillChangeRecord? found = await store.FindAsync(
            record.Request.ChangeId, CancellationToken.None);
        ExecutionEvent requested = (await this.ReplayAsync(record.Request.TraceId)).Single();

        Assert.Equal(
            (record.Diff, record.ReplacementVersion, ExecutionEventType.SkillChangeRequested,
                $"skill-change://{record.Request.ChangeId:D}"),
            (found?.Diff, found?.ReplacementVersion, requested.Type, requested.PayloadReference));
    }

    [Fact]
    public async Task CreateAsync_Should_Not_Persist_When_The_Event_Id_Is_Already_Unrelated()
    {
        await this.fixture.ResetAsync();
        SkillChangeRecord record = CreateRecord();
        ISkillChangeStore changes = this.CreateStore();
        PostgresExecutionEventStore events = this.CreateEventStore();
        await events.AppendAsync(
            new ExecutionEvent(
                record.Request.ChangeId, Guid.NewGuid(), Guid.NewGuid(), null,
                ExecutionOrigin.SelfAudit, "collision", ExecutionEventType.TraceStarted,
                ExecutionStatus.Running, at, "unrelated event"),
            CancellationToken.None);

        try
        {
            await changes.CreateAsync(record, CancellationToken.None);
        }
        catch (InvalidOperationException)
        {
        }

        Assert.Null(await changes.FindAsync(record.Request.ChangeId, CancellationToken.None));
    }

    [Fact]
    public async Task FindAsync_Should_Reject_An_Empty_Change_Id()
    {
        ISkillChangeStore store = this.CreateStore();

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.FindAsync(Guid.Empty, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_Should_Be_Idempotent_For_An_Exact_Retry()
    {
        await this.fixture.ResetAsync();
        SkillChangeRecord record = CreateRecord();
        ISkillChangeStore store = this.CreateStore();

        await store.CreateAsync(record, CancellationToken.None);
        await store.CreateAsync(record, CancellationToken.None);

        Assert.Single(await this.ReplayAsync(record.Request.TraceId));
    }

    [Fact]
    public async Task CreateAsync_Should_Converge_An_Exact_Retry_With_Submicrosecond_Time()
    {
        await this.fixture.ResetAsync();
        SkillChangeRecord source = CreateRecord();
        var record = new SkillChangeRecord(
            source.Request, source.Diff, source.ReplacementVersion, at.AddTicks(1));
        ISkillChangeStore store = this.CreateStore();

        await store.CreateAsync(record, CancellationToken.None);
        await store.CreateAsync(record, CancellationToken.None);

        Assert.Single(await this.ReplayAsync(record.Request.TraceId));
    }

    [Fact]
    public async Task CreateAsync_Should_Converge_Concurrent_Exact_Retries()
    {
        await this.fixture.ResetAsync();
        SkillChangeRecord record = CreateRecord();
        ISkillChangeStore store = this.CreateStore();

        await Task.WhenAll(
            store.CreateAsync(record, CancellationToken.None),
            store.CreateAsync(record, CancellationToken.None));

        Assert.Single(await this.ReplayAsync(record.Request.TraceId));
    }

    [Fact]
    public async Task CreateAsync_Should_Reject_A_Conflicting_Retry()
    {
        await this.fixture.ResetAsync();
        SkillChangeRecord record = CreateRecord();
        ISkillChangeStore store = this.CreateStore();
        await store.CreateAsync(record, CancellationToken.None);
        var conflict = new SkillChangeRecord(
            record.Request, "+ different", record.ReplacementVersion, record.RequestedAt);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.CreateAsync(conflict, CancellationToken.None));
    }

    [Fact]
    public async Task CreateAsync_Should_Roll_Back_When_The_Event_Fails()
    {
        await this.fixture.ResetAsync();
        SkillChangeRecord record = CreateRecord();
        ISkillChangeStore store = this.CreateStore();
        await using var rejection = await RejectingExecutionEventTrigger.CreateAsync(
            this.fixture.DataSource, DatabaseFixture.SCHEMA,
            ExecutionEventType.SkillChangeRequested);

        Exception? exception = await Record.ExceptionAsync(
            () => store.CreateAsync(record, CancellationToken.None));
        SkillChangeRecord? found = await store.FindAsync(
            record.Request.ChangeId, CancellationToken.None);

        Assert.Equal((typeof(PostgresException), true), (exception?.GetType(), found is null));
    }

    [Fact]
    public async Task FindAsync_Should_Round_Trip_The_Replacement_Document()
    {
        await this.fixture.ResetAsync();
        SkillChangeRecord record = CreateRecord();
        ISkillChangeStore store = this.CreateStore();
        await store.CreateAsync(record, CancellationToken.None);

        SkillChangeRecord? found = await store.FindAsync(
            record.Request.ChangeId, CancellationToken.None);

        Assert.Equal(record.Request.Replacement!.Body, found?.Request.Replacement?.Body);
    }

    [Fact]
    public async Task Database_Should_Reject_Mutation_Even_From_The_Ddl_Owner()
    {
        await this.fixture.ResetAsync();
        SkillChangeRecord record = CreateRecord();
        ISkillChangeStore store = this.CreateStore();
        await store.CreateAsync(record, CancellationToken.None);
        await using NpgsqlCommand command = this.fixture.DataSource.CreateCommand(
            $"update {DatabaseFixture.SCHEMA}.skill_changes set diff = 'tampered' "
            + "where change_id = @change;");
        command.Parameters.AddWithValue("change", record.Request.ChangeId);

        Exception? exception = await Record.ExceptionAsync(
            () => command.ExecuteNonQueryAsync(CancellationToken.None));

        Assert.Equal(PostgresErrorCodes.RestrictViolation, (exception as PostgresException)?.SqlState);
    }

    [Fact]
    public async Task Database_Should_Grant_The_App_Only_Insert_And_Select()
    {
        await this.fixture.ResetAsync();
        await using NpgsqlCommand command = this.fixture.DataSource.CreateCommand(
            $"""
            select has_table_privilege('dami_app', '{DatabaseFixture.SCHEMA}.skill_changes', 'select')
               and has_table_privilege('dami_app', '{DatabaseFixture.SCHEMA}.skill_changes', 'insert')
               and not has_table_privilege('dami_app', '{DatabaseFixture.SCHEMA}.skill_changes', 'update')
               and not has_table_privilege('dami_app', '{DatabaseFixture.SCHEMA}.skill_changes', 'delete')
               and not has_table_privilege('dami_app', '{DatabaseFixture.SCHEMA}.skill_changes', 'truncate')
               and not has_table_privilege('dami_app', '{DatabaseFixture.SCHEMA}.skill_changes', 'references')
               and not has_table_privilege('dami_app', '{DatabaseFixture.SCHEMA}.skill_changes', 'trigger');
            """);

        object? leastPrivilege = await command.ExecuteScalarAsync(CancellationToken.None);

        Assert.Equal(true, leastPrivilege);
    }

    [Fact]
    public async Task Database_Should_Reject_An_Unknown_Execution_Origin()
    {
        await this.fixture.ResetAsync();
        await using NpgsqlCommand command = this.fixture.DataSource.CreateCommand(
            $$"""
            insert into {{DatabaseFixture.SCHEMA}}.skill_changes
                (change_id, trace_id, span_id, origin, kind, skill_id,
                 replacement_version, replacement_document, diff, requested_at)
            values
                (@change, @trace, @span, 'Unknown', 'Author', @skill,
                 @version, '{}'::jsonb, '+ body', @at);
            """);
        command.Parameters.AddWithValue("change", Guid.NewGuid());
        command.Parameters.AddWithValue("trace", Guid.NewGuid());
        command.Parameters.AddWithValue("span", Guid.NewGuid());
        command.Parameters.AddWithValue("skill", Guid.NewGuid());
        command.Parameters.AddWithValue("version", new string('a', 64));
        command.Parameters.AddWithValue("at", at);

        Exception? exception = await Record.ExceptionAsync(
            () => command.ExecuteNonQueryAsync(CancellationToken.None));

        Assert.Equal(PostgresErrorCodes.CheckViolation, (exception as PostgresException)?.SqlState);
    }

    private static SkillChangeRecord CreateRecord()
    {
        var document = new SkillDocument(
            Guid.NewGuid(), "image-comparison", "Procedure for comparing images.",
            "# Compare images", ["vision"], [], new Dictionary<string, string>());
        var request = new SkillChangeRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, ExecutionOrigin.SelfAudit,
            SkillChangeKind.Author, document.SkillId, null, document);
        return new SkillChangeRecord(request, "+ # Compare images", new string('a', 64), at);
    }

    private ISkillChangeStore CreateStore()
    {
        return new PostgresSkillChangeStore(
            this.fixture.DataSource,
            Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA }));
    }

    private async Task<List<ExecutionEvent>> ReplayAsync(Guid traceId)
    {
        PostgresExecutionEventStore store = this.CreateEventStore();
        var events = new List<ExecutionEvent>();
        await foreach (ExecutionEvent item in store.ReplayAsync(traceId, CancellationToken.None))
        {
            events.Add(item);
        }

        return events;
    }

    private PostgresExecutionEventStore CreateEventStore()
    {
        var options = Options.Create(new PostgresOptions { SchemaName = DatabaseFixture.SCHEMA });
        return new PostgresExecutionEventStore(
            this.fixture.DataSource, options, NullLogger<PostgresExecutionEventStore>.Instance);
    }
}
