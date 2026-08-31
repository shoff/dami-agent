namespace Dami.Contracts.Domains;

/// <summary>Reads the fitness domain (H9). LocalOnly — implementations hold no egress.</summary>
public interface IFitnessStore
{
    /// <summary>Reads the whole domain, oldest first.</summary>
    Task<FitnessSnapshot> SnapshotAsync(CancellationToken cancellationToken);
}
