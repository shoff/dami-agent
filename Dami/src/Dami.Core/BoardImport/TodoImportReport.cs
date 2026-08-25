namespace Dami.Core.BoardImport;

/// <summary>What one import run did, and what it refused to do.</summary>
/// <param name="BoardId">The board written to.</param>
/// <param name="BoardCreated">True when this run created the board rather than updating it.</param>
/// <param name="TasksWritten">How many tasks the board holds.</param>
/// <param name="MutationsApplied">How many status changes this run made.</param>
/// <param name="Conflicts">
/// Tasks the file and the board disagreed about, left as the board had them.
/// </param>
/// <param name="Anomalies">What the file could not say unambiguously.</param>
public sealed record TodoImportReport(
    Guid BoardId,
    bool BoardCreated,
    int TasksWritten,
    int MutationsApplied,
    IReadOnlyList<string> Conflicts,
    IReadOnlyList<TodoAnomaly> Anomalies);
