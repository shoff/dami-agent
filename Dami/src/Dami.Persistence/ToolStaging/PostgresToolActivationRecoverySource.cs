using Dami.Contracts.ToolStaging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Dami.Persistence.ToolStaging;

/// <summary>Projects approved exact promotions into a deterministic startup-recovery batch.</summary>
public sealed class PostgresToolActivationRecoverySource : IToolActivationRecoverySource
{
    private const int MAX_LIMIT = 1_000;

    private readonly NpgsqlDataSource dataSource;
    private readonly IToolProposalStore proposals;
    private readonly string query;
    private readonly IToolVerificationStore verifications;

    /// <summary>Creates the PostgreSQL activation recovery source.</summary>
    public PostgresToolActivationRecoverySource(
        NpgsqlDataSource dataSource,
        IOptions<PostgresOptions> options,
        IToolProposalStore proposals,
        IToolVerificationStore verifications)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(proposals);
        ArgumentNullException.ThrowIfNull(verifications);
        this.dataSource = dataSource;
        this.proposals = proposals;
        this.verifications = verifications;
        string schema = options.Value.SchemaName;
        this.query = CreateQuery(schema);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ToolActivationRecoveryItem>> FindAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        if (limit is <= 0 or > MAX_LIMIT)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit), limit, $"Recovery limits must be 1–{MAX_LIMIT}.");
        }

        IReadOnlyList<RecoveryReference> references = await this.FindReferencesAsync(
            limit, cancellationToken).ConfigureAwait(false);
        var items = new List<ToolActivationRecoveryItem>(references.Count);
        for (var index = 0; index < references.Count; index++)
        {
            items.Add(await this.LoadAsync(references[index], cancellationToken)
                .ConfigureAwait(false));
        }

        return items.AsReadOnly();
    }

    private async Task<IReadOnlyList<RecoveryReference>> FindReferencesAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        await using var command = this.dataSource.CreateCommand(this.query);
        command.Parameters.AddWithValue("limit", limit);
        var references = new List<RecoveryReference>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            references.Add(new RecoveryReference(
                reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2),
                reader.GetGuid(3), reader.GetBoolean(4)));
        }

        return references.AsReadOnly();
    }

    private async Task<ToolActivationRecoveryItem> LoadAsync(
        RecoveryReference reference,
        CancellationToken cancellationToken)
    {
        StagedToolProposal proposal = await this.proposals.FindAsync(
            reference.ProposalId, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException("An approved tool proposal could not be loaded.");
        ToolVerificationRecord verification = await this.verifications.FindAsync(
            reference.ProposalId, reference.ArtifactVersion, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException("An approved tool verification could not be loaded.");
        if (verification.VerificationId != reference.VerificationId)
        {
            throw new InvalidDataException("Tool recovery loaded different verification evidence.");
        }

        return new ToolActivationRecoveryItem(
            reference.PromotionId, proposal, verification, reference.IsActivated);
    }

    private static string CreateQuery(string schema)
    {
        return $"""
            select promotion.promotion_id,
                   promotion.proposal_id,
                   promotion.artifact_version,
                   verification.verification_id,
                   exists (
                       select 1
                         from {schema}.tool_activation_outcomes outcome
                        where outcome.promotion_id = promotion.promotion_id
                          and outcome.status = 'Activated')
              from {schema}.tool_promotions promotion
              join {schema}.approvals approval
                on approval.approval_id = promotion.approval_id
               and approval.status = 'Approved'
               and approval.resolved_at is not null
              join {schema}.tool_verifications verification
                on verification.proposal_id = promotion.proposal_id
               and verification.artifact_version = promotion.artifact_version
             order by approval.resolved_at, promotion.promotion_id
             limit @limit;
            """;
    }

    private sealed record RecoveryReference(
        Guid PromotionId,
        Guid ProposalId,
        string ArtifactVersion,
        Guid VerificationId,
        bool IsActivated);
}
