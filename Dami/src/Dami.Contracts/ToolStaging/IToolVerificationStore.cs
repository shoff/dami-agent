namespace Dami.Contracts.ToolStaging;

/// <summary>Persists successful exact-artifact verification evidence.</summary>
public interface IToolVerificationStore
{
    /// <summary>Records one successful verification idempotently with its event.</summary>
    Task<ToolVerificationRecord> RecordAsync(
        ToolVerificationRecord record,
        CancellationToken cancellationToken);

    /// <summary>Finds verification evidence for one exact staged artifact.</summary>
    Task<ToolVerificationRecord?> FindAsync(
        Guid proposalId,
        string artifactVersion,
        CancellationToken cancellationToken);
}
