using Dami.Contracts.Briefs;
using Dami.Contracts.Domains;
using Dami.Contracts.Gateways;
using Dami.Contracts.Capabilities;
using Dami.Contracts.Events;
using Dami.Contracts.FilePatches;
using Dami.Contracts.Memory;
using Dami.Contracts.Approvals;
using Dami.Contracts.Proactive;
using Dami.Contracts.Sessions;
using Dami.Contracts.ToolStaging;
using Dami.Contracts.Privacy;
using Dami.Contracts.TaskBoard;
using Dami.Contracts.Scheduling;
using Dami.Persistence.Approvals;
using Dami.Persistence.Briefs;
using Dami.Persistence.Domains;
using Dami.Persistence.Gateways;
using Dami.Persistence.Capabilities;
using Dami.Persistence.Events;
using Dami.Persistence.FilePatches;
using Dami.Persistence.Memory;
using Dami.Persistence.Proactive;
using Dami.Persistence.Sessions;
using Dami.Persistence.Skills;
using Dami.Persistence.ToolStaging;
using Dami.Persistence.Privacy;
using Dami.Persistence.TaskBoard;
using Dami.Persistence.Scheduling;
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
        services.TryAddSingleton<IProactiveRunHistory, PostgresProactiveRunLog>();
        services.TryAddSingleton<ISchemaLedger, PostgresSchemaLedger>();
        services.TryAddSingleton<IObservationCorpus, PostgresObservationCorpus>();
        services.TryAddSingleton<IConclusionLedger, PostgresConclusionLedger>();
        services.TryAddSingleton<IPushbackLedger, PostgresPushbackLedger>();
        services.TryAddSingleton<IObservationEmbeddingStore, PostgresObservationEmbeddingStore>();
        services.TryAddSingleton<IApprovalService, PostgresApprovalService>();
        services.TryAddSingleton<IConclusionEmbeddingStore, PostgresConclusionEmbeddingStore>();
        services.TryAddSingleton<IEgressMeter, PostgresEgressMeter>();
        services.TryAddSingleton<IEgressBriefStore, PostgresEgressBriefStore>();
        services.TryAddSingleton<IDisclosureLedger, PostgresDisclosureLedger>();
        services.TryAddSingleton<ITaskBoardStore, PostgresTaskBoardStore>();
        services.TryAddSingleton<IScheduledJobStore, PostgresScheduledJobStore>();
        services.TryAddSingleton<IObservationCurationStore, PostgresObservationCurationStore>();
        RegisterProposalStores(services);
        RegisterSkillChangeStore(services);
        RegisterDomainAndSessionStores(services);

        return services;
    }

    private static void RegisterDomainAndSessionStores(IServiceCollection services)
    {
        services.TryAddSingleton<IHealthEventStore, PostgresHealthEventStore>();
        services.TryAddSingleton<IFitnessStore, PostgresFitnessStore>();
        services.TryAddSingleton<IDomainFactStore, PostgresDomainFactStore>();
        foreach (var domain in new[] { "network", "civic", "estate", "workshop" })
        {
            var name = domain;
            services.AddSingleton<Dami.Contracts.Context.IStructuredFactSource>(
                provider => new DomainFactSource(provider.GetRequiredService<IDomainFactStore>(), name));
        }


        // The same instance in both roles: the domain that records health events is the
        // domain that hands them to retrieval.
        services.AddSingleton<Dami.Contracts.Context.IStructuredFactSource>(
            provider => (PostgresHealthEventStore)provider.GetRequiredService<IHealthEventStore>());
        services.TryAddSingleton<IGatewayAuthority, PostgresGatewayAuthority>();
        services.TryAddSingleton<ICapabilityEmbeddingStore, PostgresCapabilityEmbeddingStore>();
        services.TryAddSingleton<PostgresSessionStore>();
        services.TryAddSingleton<IConversationSessionStore>(provider =>
            provider.GetRequiredService<PostgresSessionStore>());
        services.TryAddSingleton<IConversationTurnStore>(provider =>
            provider.GetRequiredService<PostgresSessionStore>());
    }

    private static void RegisterSkillChangeStore(IServiceCollection services)
    {
        services.TryAddSingleton<PostgresSkillChangeStore>();
        services.TryAddSingleton<ISkillChangeStore>(provider =>
            provider.GetRequiredService<PostgresSkillChangeStore>());
        services.TryAddSingleton<ISkillChangeRecoveryStore>(provider =>
            provider.GetRequiredService<PostgresSkillChangeStore>());
    }

    private static void RegisterProposalStores(IServiceCollection services)
    {
        services.TryAddSingleton<IFilePatchProposalStore, PostgresFilePatchProposalStore>();
        services.TryAddSingleton<IToolProposalStore, PostgresToolProposalStore>();
        services.TryAddSingleton<IToolPromotionStore, PostgresToolPromotionStore>();
        services.TryAddSingleton<IToolVerificationStore, PostgresToolVerificationStore>();
        services.TryAddSingleton<IToolActivationStore, PostgresToolActivationStore>();
        services.TryAddSingleton<
            IToolActivationRecoverySource, PostgresToolActivationRecoverySource>();
    }
}
