using System.Data;
using System.Runtime.CompilerServices;
using Dami.Contracts.Context;
using Dami.Contracts.Domains;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dami.Persistence.Domains;

/// <summary>PostgreSQL store for the shared domain-fact table (migration 033).</summary>
public sealed class PostgresDomainFactStore : IDomainFactStore
{
    private readonly NpgsqlDataSource dataSource;
    private readonly string schema;

    /// <summary>Creates the store.</summary>
    public PostgresDomainFactStore(NpgsqlDataSource dataSource, IOptions<PostgresOptions> options)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);
        this.dataSource = dataSource;
        this.schema = options.Value.SchemaName;
    }

    /// <inheritdoc />
    public async Task<bool> RecordAsync(DomainFact fact, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(fact);
        await using var command = this.dataSource.CreateCommand(
            $"""
            insert into {this.schema}.domain_facts
                (fact_id, domain, as_of, category, description, source, recorded_at)
            values (@id, @domain, @asOf, @category, @description, @source, @recorded)
            on conflict (domain, as_of, description) do nothing;
            """);
        command.Parameters.AddWithValue("id", fact.FactId);
        command.Parameters.AddWithValue("domain", fact.Domain);
        command.Parameters.AddWithValue("asOf", fact.AsOf);
        command.Parameters.AddWithValue("category", fact.Category);
        command.Parameters.AddWithValue("description", fact.Description);
        command.Parameters.AddWithValue("source", fact.Source);
        command.Parameters.AddWithValue("recorded", fact.RecordedAt);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    /// <inheritdoc />
    public IAsyncEnumerable<DomainFact> TimelineAsync(string? domain, int limit, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        var command = this.dataSource.CreateCommand(
            $"""
            select f.fact_id, f.domain, f.as_of, f.category, f.description, f.source, f.recorded_at
              from {this.schema}.domain_facts f
             where (@domain is null or f.domain = @domain)
               and not exists (select 1 from {this.schema}.domain_fact_rejections r where r.fact_id = f.fact_id)
             order by f.as_of desc, f.recorded_at desc
             limit @limit;
            """);
        command.Parameters.AddWithValue("domain", NpgsqlTypes.NpgsqlDbType.Text, (object?)domain ?? DBNull.Value);
        command.Parameters.AddWithValue("limit", limit);
        return StreamAsync(command, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<DomainFact> BetweenAsync(
        string domain, DateOnly from, DateOnly to, int limit, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        var command = this.dataSource.CreateCommand(
            $"""
            select f.fact_id, f.domain, f.as_of, f.category, f.description, f.source, f.recorded_at
              from {this.schema}.domain_facts f
             where f.domain = @domain and f.as_of between @from and @to
               and not exists (select 1 from {this.schema}.domain_fact_rejections r where r.fact_id = f.fact_id)
             order by f.as_of, f.description
             limit @limit;
            """);
        command.Parameters.AddWithValue("domain", domain);
        command.Parameters.AddWithValue("from", from);
        command.Parameters.AddWithValue("to", to);
        command.Parameters.AddWithValue("limit", limit);
        return StreamAsync(command, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> RejectAsync(Guid factId, string reason, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        await using var command = this.dataSource.CreateCommand(
            $"""
            insert into {this.schema}.domain_fact_rejections (fact_id, reason)
            select fact_id, @reason from {this.schema}.domain_facts where fact_id = @id
            on conflict (fact_id) do nothing;
            """);
        command.Parameters.AddWithValue("id", factId);
        command.Parameters.AddWithValue("reason", reason);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<(string Domain, int Facts)>> DomainsAsync(CancellationToken cancellationToken)
    {
        await using var command = this.dataSource.CreateCommand(
            $"""
            select f.domain, count(*)::integer
              from {this.schema}.domain_facts f
             where not exists (select 1 from {this.schema}.domain_fact_rejections r where r.fact_id = f.fact_id)
             group by f.domain order by f.domain;
            """);
        var domains = new List<(string, int)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            domains.Add((reader.GetString(0), reader.GetInt32(1)));
        }

        return domains;
    }

    private static async IAsyncEnumerable<DomainFact> StreamAsync(
        NpgsqlCommand command,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using (command.ConfigureAwait(false))
        {
            await using var reader = await command
                .ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return Read(reader);
            }
        }
    }

    private static DomainFact Read(NpgsqlDataReader reader)
    {
        return new DomainFact(
            reader.GetGuid(0), reader.GetString(1), reader.GetFieldValue<DateOnly>(2),
            reader.GetString(3), reader.GetString(4), reader.GetString(5),
            reader.GetFieldValue<DateTimeOffset>(6));
    }
}

/// <summary>One domain of the shared store, as retrieval sees it (ADR-0019 routing).</summary>
public sealed class DomainFactSource : IStructuredFactSource
{
    private readonly IDomainFactStore store;

    /// <summary>Creates the source for one domain.</summary>
    public DomainFactSource(IDomainFactStore store, string domain)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        this.store = store;
        this.Domain = domain;
    }

    /// <inheritdoc />
    public string Domain { get; }

    /// <inheritdoc />
    public async IAsyncEnumerable<StructuredFact> RelevantAsync(
        string request,
        int limit,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await foreach (var fact in this.store.TimelineAsync(this.Domain, limit, cancellationToken)
            .ConfigureAwait(false))
        {
            yield return new StructuredFact(fact.FactId, fact.Description, fact.AsOf, fact.Category);
        }
    }
}
