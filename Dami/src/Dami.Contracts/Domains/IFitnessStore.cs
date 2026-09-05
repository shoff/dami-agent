namespace Dami.Contracts.Domains;

/// <summary>Reads the fitness domain (H9). LocalOnly — implementations hold no egress.</summary>
public interface IFitnessStore
{
    /// <summary>Reads the whole domain, oldest first.</summary>
    Task<FitnessSnapshot> SnapshotAsync(CancellationToken cancellationToken);

    /// <summary>Records a resistance session; the exercise is found by name or created. Returns the event id.</summary>
    Task<Guid> RecordResistanceAsync(FitnessResistanceEntry entry, CancellationToken cancellationToken);

    /// <summary>Records a cardio session. Returns the event id.</summary>
    Task<Guid> RecordCardioAsync(FitnessCardioEntry entry, CancellationToken cancellationToken);
}
