namespace Dami.Contracts.Anticoag;

/// <summary>An INR reading Steve reported.</summary>
public sealed record InrReading(Guid EntryId, DateOnly OnDay, decimal Inr, DateTimeOffset RecordedAt);

/// <summary>A warfarin dose instruction Steve reported, verbatim. Dami never proposes one (ADR-0037).</summary>
public sealed record DoseChange(Guid EntryId, DateOnly OnDay, string Dose, DateTimeOffset RecordedAt);

/// <summary>Steve's INR readings and dose changes. Health data: local only, never sent to a model.</summary>
public interface IAnticoagLog
{
    /// <summary>Records a reading.</summary>
    Task RecordAsync(InrReading reading, CancellationToken cancellationToken);

    /// <summary>Records a dose change.</summary>
    Task RecordAsync(DoseChange dose, CancellationToken cancellationToken);

    /// <summary>The latest readings, newest first.</summary>
    Task<IReadOnlyList<InrReading>> ReadingsAsync(int limit, CancellationToken cancellationToken);

    /// <summary>The latest dose changes, newest first.</summary>
    Task<IReadOnlyList<DoseChange>> DosesAsync(int limit, CancellationToken cancellationToken);
}
