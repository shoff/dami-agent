namespace Dami.Contracts.Scheduling;

/// <summary>The kind of work a schedule invokes.</summary>
public enum ScheduledJobKind
{
    /// <summary>A prompt handled by Dami's traced runtime.</summary>
    Prompt,

    /// <summary>An exact executable and argument vector on the local host.</summary>
    Command,
}

/// <summary>The lifecycle state of a scheduled job.</summary>
public enum ScheduledJobStatus
{
    /// <summary>Proposed but inert until explicitly confirmed.</summary>
    Draft,

    /// <summary>Eligible to run on schedule.</summary>
    Active,

    /// <summary>Deliberately disabled.</summary>
    Paused,
}

/// <summary>A model-generated proposal shown to the user before activation.</summary>
public sealed record ScheduledJobProposal(
    string Name,
    string Description,
    ScheduledJobKind Kind,
    string Payload,
    IReadOnlyList<string> Arguments,
    string CronExpression,
    string TimeZoneId);

/// <summary>A durable recurring job and its latest scheduling state.</summary>
public sealed record ScheduledJob(
    Guid JobId,
    string Name,
    string Description,
    ScheduledJobKind Kind,
    string Payload,
    IReadOnlyList<string> Arguments,
    string CronExpression,
    string TimeZoneId,
    ScheduledJobStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset? NextRunAt,
    DateTimeOffset? LastRunAt,
    string? LastRunStatus);
