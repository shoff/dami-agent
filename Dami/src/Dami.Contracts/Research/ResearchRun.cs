using Dami.Contracts.Events;

namespace Dami.Contracts.Research;

/// <summary>Whether a retained answer reached the end of its stream.</summary>
public enum ResearchAnswerStatus
{
    /// <summary>No answer has been archived.</summary>
    None,
    /// <summary>The answer stream completed.</summary>
    Complete,
    /// <summary>Only part of the answer was retained.</summary>
    Partial,
}

/// <summary>The observed outcome of a research traversal.</summary>
public enum ResearchRunStatus
{
    /// <summary>Source collection is in progress.</summary>
    Running,
    /// <summary>The bounded traversal finished.</summary>
    Completed,
    /// <summary>The traversal failed unexpectedly.</summary>
    Failed,
    /// <summary>The caller stopped the traversal.</summary>
    Cancelled,
    /// <summary>The owning runtime stopped before recording an outcome.</summary>
    Interrupted,
}

/// <summary>A durable snapshot of one bounded research traversal.</summary>
public sealed record ResearchRun(Guid RunId, Guid TraceId, string Question, Uri Seed, DateTimeOffset StartedAt)
{
    /// <summary>The frontier chose the starting sites because none was supplied.</summary>
    public bool AutomaticSources { get; init; }

    /// <summary>Starting sites retained together in this run; empty in older single-site archives.</summary>
    public IReadOnlyList<Uri> StartingUrls { get; init; } = [];

    /// <summary>Monotonic version; a replay cannot replace newer progress.</summary>
    public int Revision { get; init; } = 1;

    /// <summary>Time of the latest persisted progress.</summary>
    public DateTimeOffset UpdatedAt { get; init; } = StartedAt;

    /// <summary>What initiated the research.</summary>
    public ExecutionOrigin Origin { get; init; } = ExecutionOrigin.UserTurn;

    /// <summary>Current collection outcome.</summary>
    public ResearchRunStatus Status { get; init; }

    /// <summary>How many reads were attempted, including skipped reads.</summary>
    public int PagesAttempted { get; init; }

    /// <summary>The runtime instance that owns this traversal.</summary>
    public Guid OwnerId { get; init; }

    /// <summary>The source currently being read, if any.</summary>
    public Uri? CurrentUrl { get; init; }

    /// <summary>References encountered in saved pages.</summary>
    public int ReferencesConsidered { get; init; }

    /// <summary>Whether the page budget left candidates unread.</summary>
    public bool PageLimitReached { get; init; }

    /// <summary>Reads that were skipped and their reported reasons.</summary>
    public IReadOnlyList<ResearchIssue> Issues { get; init; } = [];

    /// <summary>An explanation of a failed, cancelled or interrupted outcome.</summary>
    public string? Error { get; init; }

    /// <summary>The conversation's answer, separate from source text.</summary>
    public string? Answer { get; init; }

    /// <summary>Whether the saved answer is complete.</summary>
    public ResearchAnswerStatus AnswerStatus { get; init; }

    /// <summary>The archive's size limit cut off the answer.</summary>
    public bool AnswerTruncated { get; init; }

    /// <summary>The source text and provenance retained from successful reads.</summary>
    public IReadOnlyList<ResearchFinding> Findings { get; init; } = [];

    /// <summary>A lightweight history entry without source bodies.</summary>
    public ResearchRunSummary Summarize() => new(this.RunId, this.TraceId, this.Question, this.Seed,
        this.StartedAt, this.UpdatedAt, this.Revision, this.Status, this.Findings.Count);
}

/// <summary>A source that could not be retained.</summary>
public sealed record ResearchIssue(Uri Url, string Reason);

/// <summary>Small run metadata suitable for polling a research library.</summary>
public sealed record ResearchRunSummary(Guid RunId, Guid TraceId, string Question, Uri Seed,
    DateTimeOffset StartedAt, DateTimeOffset UpdatedAt, int Revision, ResearchRunStatus Status, int SourceCount);

/// <summary>Stores research artifacts independently of a client's lifetime.</summary>
public interface IResearchRunStore
{
    /// <summary>Persists the current snapshot.</summary>
    Task SaveAsync(ResearchRun run, CancellationToken cancellationToken);

    /// <summary>Gets a saved run, including its source text.</summary>
    Task<ResearchRun?> GetAsync(Guid runId, CancellationToken cancellationToken);

    /// <summary>Reads the newest bounded history without source bodies.</summary>
    Task<IReadOnlyList<ResearchRunSummary>> ListAsync(int limit, CancellationToken cancellationToken);

    /// <summary>Finds the latest research artifact created by a conversation trace.</summary>
    Task<ResearchRun?> FindByTraceAsync(Guid traceId, CancellationToken cancellationToken);
}
