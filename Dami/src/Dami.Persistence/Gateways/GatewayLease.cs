using Dami.Contracts.Gateways;
using Npgsql;

namespace Dami.Persistence.Gateways;

/// <summary>An held gateway authority. Disposing ends the session and frees the lock.</summary>
internal sealed class GatewayLease : IGatewayLease
{
    private readonly NpgsqlConnection connection;
    private readonly string schema;

    internal GatewayLease(NpgsqlConnection connection, string gatewayName, string schema)
    {
        this.connection = connection;
        this.GatewayName = gatewayName;
        this.schema = schema;
    }

    /// <inheritdoc />
    public string GatewayName { get; }

    /// <inheritdoc />
    public async Task HeartbeatAsync(CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"""
            insert into {this.schema}.gateway_authority
                (gateway_name, holder_host, holder_pid, acquired_at, heartbeat_at)
            values (@name, @host, @pid, now(), now())
            on conflict (gateway_name) do update
                set holder_host = excluded.holder_host,
                    holder_pid = excluded.holder_pid,
                    heartbeat_at = now();
            """,
            this.connection);
        command.Parameters.AddWithValue("name", this.GatewayName);
        command.Parameters.AddWithValue("host", Environment.MachineName);
        command.Parameters.AddWithValue("pid", Environment.ProcessId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // Disposing an NpgsqlConnection returns it to the pool rather than ending the
        // session, so the advisory lock would survive a graceful shutdown and lock out
        // the next instance. Release it explicitly. A crashed process needs no such
        // care: its sockets close, Postgres ends the sessions, and the lock frees.
        try
        {
            await using var release = new NpgsqlCommand(
                "select pg_advisory_unlock(hashtext('dami.gateway:' || @name));", this.connection);
            release.Parameters.AddWithValue("name", this.GatewayName);
            await release.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        catch (NpgsqlException)
        {
            // The session is already gone, which frees the lock anyway.
        }

        await this.connection.DisposeAsync().ConfigureAwait(false);
    }
}
