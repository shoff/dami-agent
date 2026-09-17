namespace Dami.Contracts.Domains;

/// <summary>
/// Something on this host that can be read as one or more daily series: the gym log,
/// the conversation log, the repository. Local-only by construction — a source reads
/// tables and files on this machine and nothing leaves.
/// </summary>
public interface IDailySeriesSource
{
    /// <summary>The source's series over an inclusive window of local days.</summary>
    /// <param name="from">The first day, inclusive.</param>
    /// <param name="to">The last day, inclusive.</param>
    /// <param name="zone">The owner's time zone; every source buckets by the same one.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<IReadOnlyList<DailySeries>> ReadAsync(
        DateOnly from,
        DateOnly to,
        TimeZoneInfo zone,
        CancellationToken cancellationToken);
}
