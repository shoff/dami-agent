using Dami.Contracts.Gateways;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dami.Persistence.Gateways;

/// <summary>Gateway authority as a Postgres session advisory lock.</summary>
/// <remarks>
/// The lock lives on a dedicated connection held for the lease's lifetime. That gives
/// the property that matters: if the holder crashes, the session ends and Postgres
/// releases the lock, so the next instance can take over without anyone clearing a
/// stale flag by hand. A row in <c>gateway_authority</c> records who holds it for
/// operators; the row is bookkeeping, the lock is the truth.
/// </remarks>
public sealed class PostgresGatewayAuthority : IGatewayAuthority
{
    private readonly string connectionString;
    private readonly PostgresOptions storeOptions;
    private readonly ILogger<PostgresGatewayAuthority> logger;

    /// <summary>Creates the authority.</summary>
    public PostgresGatewayAuthority(
        NpgsqlDataSource dataSource,
        IOptions<PostgresOptions> storeOptions,
        ILogger<PostgresGatewayAuthority> logger)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(storeOptions);
        ArgumentNullException.ThrowIfNull(logger);

        this.connectionString = dataSource.ConnectionString;
        this.storeOptions = storeOptions.Value;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<IGatewayLease?> TryAcquireAsync(
        string gatewayName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(gatewayName);

        var connection = new NpgsqlConnection(this.connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!await TryLockAsync(connection, gatewayName, cancellationToken).ConfigureAwait(false))
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                this.logger.LogWarning(
                    "Gateway {Gateway}: another process holds authority; this instance will not serve",
                    gatewayName);
                return null;
            }
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        var lease = new GatewayLease(connection, gatewayName, this.storeOptions.SchemaName);
        await lease.HeartbeatAsync(cancellationToken).ConfigureAwait(false);
        this.logger.LogInformation("Gateway {Gateway}: authority acquired", gatewayName);
        return lease;
    }

    private static async Task<bool> TryLockAsync(
        NpgsqlConnection connection,
        string gatewayName,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "select pg_try_advisory_lock(hashtext('dami.gateway:' || @name));", connection);
        command.Parameters.AddWithValue("name", gatewayName);
        var granted = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return granted is true;
    }
}
