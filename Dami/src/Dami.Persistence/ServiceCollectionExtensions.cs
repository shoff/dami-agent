using Dami.Contracts.Capabilities;
using Dami.Contracts.Events;
using Dami.Contracts.Memory;
using Dami.Contracts.Approvals;
using Dami.Contracts.Proactive;
using Dami.Persistence.Approvals;
using Dami.Persistence.Capabilities;
using Dami.Persistence.Events;
using Dami.Persistence.Memory;
using Dami.Persistence.Proactive;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Dami.Persistence;

/// <summary>Registers the persistence layer in a composition root.</summary>
/// <remarks>
/// One method, deliberately, because D-012 makes the composition root the auditable
/// record of who can touch what. A host that calls this gains the event store, the
/// corpus, and the ledgers — and nothing here receives or registers an egress client,
/// which is the visible shape of "local-only services have no egress client at all".
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>Adds the PostgreSQL-backed stores.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="connectionString">
    /// The connection string for the runtime role. Comes from user-secrets or the
    /// environment — never from a file in the working tree.
    /// </param>
    public static IServiceCollection AddDamiPersistence(
        this IServiceCollection services,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connectionString);

        services.TryAddSingleton(_ => NpgsqlDataSource.Create(connectionString));
        services.AddOptions<PostgresOptions>();

        services.AddOptions<ProactiveOptions>();

        services.TryAddSingleton<IExecutionEventStore, PostgresExecutionEventStore>();
        services.TryAddSingleton<ISurfacingQueue, PostgresSurfacingQueue>();
        services.TryAddSingleton<IProactiveRunLog, PostgresProactiveRunLog>();
        services.TryAddSingleton<IObservationCorpus, PostgresObservationCorpus>();
        services.TryAddSingleton<IConclusionLedger, PostgresConclusionLedger>();
        services.TryAddSingleton<IPushbackLedger, PostgresPushbackLedger>();
        services.TryAddSingleton<IObservationEmbeddingStore, PostgresObservationEmbeddingStore>();
        services.TryAddSingleton<IApprovalService, PostgresApprovalService>();
        services.TryAddSingleton<IConclusionEmbeddingStore, PostgresConclusionEmbeddingStore>();
        services.TryAddSingleton<ICapabilityEmbeddingStore, PostgresCapabilityEmbeddingStore>();

        return services;
    }
}
