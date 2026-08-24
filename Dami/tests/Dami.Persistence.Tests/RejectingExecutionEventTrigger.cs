using Dami.Contracts.Events;
using Npgsql;

namespace Dami.Persistence.Tests;

/// <summary>Scoped PostgreSQL fault injection for approval transaction tests.</summary>
internal sealed class RejectingExecutionEventTrigger : IAsyncDisposable
{
    private readonly NpgsqlDataSource dataSource;
    private readonly string schema;

    private RejectingExecutionEventTrigger(NpgsqlDataSource dataSource, string schema)
    {
        this.dataSource = dataSource;
        this.schema = schema;
    }

    public static async Task<RejectingExecutionEventTrigger> CreateAsync(
        NpgsqlDataSource dataSource,
        string schema,
        ExecutionEventType type)
    {
        var rejection = new RejectingExecutionEventTrigger(dataSource, schema);
        await using var command = dataSource.CreateCommand(rejection.InstallSql(type));
        await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
        return rejection;
    }

    public async ValueTask DisposeAsync()
    {
        await using var command = this.dataSource.CreateCommand(
            $"""
            drop trigger if exists reject_test_event on {this.schema}.execution_events;
            drop function if exists {this.schema}.reject_test_event();
            """);
        await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private string InstallSql(ExecutionEventType type)
    {
        return $"""
            drop trigger if exists reject_test_event on {this.schema}.execution_events;
            create or replace function {this.schema}.reject_test_event() returns trigger
            language plpgsql as $function$
            begin
                if new.type = '{type}' then
                    raise exception 'test rejected %', new.type using errcode = 'check_violation';
                end if;
                return new;
            end;
            $function$;
            create trigger reject_test_event before insert
                on {this.schema}.execution_events
                for each row execute function {this.schema}.reject_test_event();
            """;
    }
}
