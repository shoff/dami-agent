using System.Globalization;
using System.Text.Json;
using Dami.Contracts.Models;
using Dami.Contracts.Scheduling;
using Dami.Core.Scheduling;
using Microsoft.Extensions.Logging;

namespace Dami.Core.Frontier;

/// <summary>Lets the frontier draft a recurring job and, on Steve's word, activate it (ADR-0030).</summary>
public interface IFrontierScheduling
{
    /// <summary>The drafting tool.</summary>
    FrontierTool ScheduleTool { get; }

    /// <summary>The confirming tool.</summary>
    FrontierTool ConfirmTool { get; }

    /// <summary>Creates a Draft; nothing runs until it is confirmed.</summary>
    Task<FrontierToolResult> ScheduleAsync(
        string channel, JsonElement arguments, CancellationToken cancellationToken);

    /// <summary>Activates a Draft by its short id, after Steve said yes.</summary>
    Task<FrontierToolResult> ConfirmAsync(string draftId, CancellationToken cancellationToken);
}

/// <summary>
/// Two tools, because a job that will run unattended needs Steve's explicit yes and the
/// frontier cannot give it on his behalf. <c>schedule</c> writes a Draft and hands back a
/// short id; the frontier tells Steve what it is and asks; when he confirms, the next
/// turn calls <c>confirm_schedule</c> with that id. Only Prompt jobs are offered — a
/// Command job runs an executable, and that stays a GUI-and-planner decision (G16).
/// </summary>
public sealed class ScheduleTools : IFrontierScheduling
{
    /// <summary>The drafting tool's name.</summary>
    public const string SCHEDULE = "schedule";

    /// <summary>The confirming tool's name.</summary>
    public const string CONFIRM = "confirm_schedule";

    private const int SHORT_ID_LENGTH = 8;

    private readonly ScheduledJobService service;
    private readonly IScheduledJobStore store;
    private readonly ILogger<ScheduleTools> logger;

    /// <summary>Creates the tools.</summary>
    public ScheduleTools(ScheduledJobService service, IScheduledJobStore store, ILogger<ScheduleTools> logger)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(logger);
        this.service = service;
        this.store = store;
        this.logger = logger;
    }

    /// <inheritdoc />
    public FrontierTool ScheduleTool { get; } = new(
        SCHEDULE,
        "Draft a recurring job that will run one of Steve's requests on a schedule and deliver "
        + "the result to this channel — a daily picture, a weekly summary, a reminder. It is NOT "
        + "active until Steve confirms: tell him the draft's short id, exactly what will run and "
        + "when, and ask him to confirm. Cron is five fields; Steve is in America/Chicago unless "
        + "he says otherwise.",
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                name = new { type = "string", description = "A short name, e.g. 'morning portrait'." },
                description = new { type = "string", description = "One sentence: what it does for him." },
                request = new { type = "string", description = "The request to run each time, as Steve would phrase it to you." },
                cron = new { type = "string", description = "Five-field cron expression, e.g. '0 7 * * *'." },
                timeZoneId = new { type = "string", description = "IANA time zone, e.g. 'America/Chicago'." },
            },
            required = new[] { "name", "description", "request", "cron", "timeZoneId" },
            additionalProperties = false,
        }));

    /// <inheritdoc />
    public FrontierTool ConfirmTool { get; } = new(
        CONFIRM,
        "Activate a drafted job after Steve has explicitly said yes to it. Pass the draft's "
        + "short id from the schedule tool. Never call this without his confirmation in this "
        + "conversation.",
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new { draftId = new { type = "string", description = "The short id the schedule tool returned." } },
            required = new[] { "draftId" },
            additionalProperties = false,
        }));

    /// <inheritdoc />
    public async Task<FrontierToolResult> ScheduleAsync(
        string channel, JsonElement arguments, CancellationToken cancellationToken)
    {
        var proposal = new ScheduledJobProposal(
            Argument(arguments, "name"), Argument(arguments, "description"), ScheduledJobKind.Prompt,
            Argument(arguments, "request"), [], Argument(arguments, "cron"), Argument(arguments, "timeZoneId"),
            channel);
        var draft = await this.service.CreateDraftAsync(proposal, cancellationToken).ConfigureAwait(false);
        this.logger.LogInformation("Drafted job {Id} '{Name}' for {Channel}", draft.JobId, draft.Name, channel);
        return FrontierToolResult.Ok(
            $"Draft {ShortId(draft)} created, not active: '{draft.Name}' runs \"{draft.Payload}\" on "
            + $"cron '{draft.CronExpression}' ({draft.TimeZoneId}), delivered here. Tell Steve this and "
            + $"ask him to confirm; when he says yes, call {CONFIRM} with draftId {ShortId(draft)}.");
    }

    /// <inheritdoc />
    public async Task<FrontierToolResult> ConfirmAsync(string draftId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(draftId);
        var drafts = (await this.store.ListAsync(cancellationToken).ConfigureAwait(false))
            .Where(job => job.Status == ScheduledJobStatus.Draft
                && job.JobId.ToString("N").StartsWith(draftId.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (drafts.Count != 1)
        {
            return FrontierToolResult.Failed(drafts.Count == 0
                ? $"no draft job starts with '{draftId}'"
                : $"'{draftId}' matches {drafts.Count} drafts; use a longer id");
        }

        var active = await this.service.ConfirmAsync(drafts[0].JobId, cancellationToken).ConfigureAwait(false);
        this.logger.LogInformation("Activated job {Id} '{Name}'", active.JobId, active.Name);
        return FrontierToolResult.Ok(
            $"Active: '{active.Name}' — next run {active.NextRunAt?.ToString("u", CultureInfo.InvariantCulture)}.");
    }

    private static string ShortId(ScheduledJob job) => job.JobId.ToString("N")[..SHORT_ID_LENGTH];

    private static string Argument(JsonElement arguments, string name)
    {
        var value = arguments.TryGetProperty(name, out var element) ? element.GetString() : null;
        return string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"the tool needs a non-empty '{name}'")
            : value.Trim();
    }
}
