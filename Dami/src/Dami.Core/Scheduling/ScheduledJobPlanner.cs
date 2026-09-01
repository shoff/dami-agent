using System.Text.Json;
using System.Text.Json.Serialization;
using Dami.Contracts.Models;
using Dami.Contracts.Scheduling;

namespace Dami.Core.Scheduling;

/// <summary>One interview turn: either another question or a complete inert proposal.</summary>
public sealed record ScheduledJobPlanningReply(string? Question, ScheduledJobProposal? Proposal);

/// <summary>Interviews the user locally and produces a structured schedule proposal.</summary>
public sealed class ScheduledJobPlanner
{
    private static readonly JsonSerializerOptions options = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IChatClient chatClient;

    /// <summary>Creates the local scheduling planner.</summary>
    public ScheduledJobPlanner(IChatClient chatClient)
    {
        this.chatClient = chatClient;
    }

    /// <summary>Asks the next useful question or returns a proposal ready to confirm.</summary>
    public async Task<ScheduledJobPlanningReply> PlanAsync(
        IReadOnlyList<string> conversation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        if (conversation.Count == 0 || conversation.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("The scheduling conversation cannot be empty.", nameof(conversation));
        }

        var response = await this.chatClient.CompleteAsync(
            Prompt(JsonSerializer.Serialize(conversation)), cancellationToken).ConfigureAwait(false);
        var reply = JsonSerializer.Deserialize<ScheduledJobPlanningReply>(response, options)
            ?? throw new InvalidOperationException("The scheduler planner returned JSON null.");
        if ((reply.Question is null) == (reply.Proposal is null))
        {
            throw new InvalidOperationException("The planner must return exactly one question or proposal.");
        }

        return reply;
    }

    private static string Prompt(string conversation) => $$$"""
        You are helping a user create a recurring scheduled job. Interview them until you
        know what runs, when it runs, and in which time zone. Jobs may be Prompt (payload is
        a Dami request; arguments is []) or Command (payload is an absolute executable path;
        arguments is an exact argument array; never invent an implicit shell command).
        Return JSON only. Return either {"question":"...","proposal":null} or
        {"question":null,"proposal":{"name":"...","description":"...","kind":"Prompt|Command",
        "payload":"...","arguments":[],"cronExpression":"five fields","timeZoneId":"IANA id"}}.
        Ask one concise question at a time. Do not propose until ambiguities are resolved.
        Conversation, alternating user and assistant entries: {{{conversation}}}
        """;
}
