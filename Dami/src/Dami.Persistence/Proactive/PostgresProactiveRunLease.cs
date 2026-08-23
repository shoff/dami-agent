using Dami.Contracts.Proactive;
using Npgsql;

namespace Dami.Persistence.Proactive;

/// <summary>Releases one PostgreSQL-backed service lease.</summary>
internal sealed class PostgresProactiveRunLease : IProactiveRunLease
{
    private readonly NpgsqlDataSource dataSource;
    private readonly string table;
    private readonly string serviceName;
    private readonly Guid leaseId;
    private int disposed;

    public PostgresProactiveRunLease(
        NpgsqlDataSource dataSource,
        string table,
        string serviceName,
        Guid leaseId)
    {
        this.dataSource = dataSource;
        this.table = table;
        this.serviceName = serviceName;
        this.leaseId = leaseId;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref this.disposed, 1) != 0)
        {
            return;
        }

        await using var command = this.dataSource.CreateCommand(
            $"delete from {this.table} where service_name = @service_name and lease_id = @lease_id;");
        command.Parameters.AddWithValue("service_name", this.serviceName);
        command.Parameters.AddWithValue("lease_id", this.leaseId);
        await command.ExecuteNonQueryAsync(CancellationToken.None).ConfigureAwait(false);
    }
}
