using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Core.Frontier;

/// <summary>The local sidecar deciding what may leave, per item, in three ways.</summary>
/// <remarks>
/// This is the local model doing exactly the mundane work it is good at: reading Steve's
/// rules and applying them to text. It never answers the question — it only decides what
/// the frontier is allowed to see, and rewrites what needs disguising.
///
/// Failure is closed: if the model returns something unparseable, every item is
/// withheld. A privacy gate that fails open is worse than no gate, because it looks like
/// protection.
/// </remarks>
public sealed class LocalDisclosureGate : IContextDisclosureGate
{
    /// <summary>How many of Steve's most recent corrections ride in the prompt.</summary>
    private const int EXAMPLE_LIMIT = 20;

    private readonly IChatClient chatClient;
    private readonly IDisclosureLedger ledger;
    private readonly DisclosureOptions gateOptions;
    private readonly ILogger<LocalDisclosureGate> logger;
    private readonly Regex nameMask;

    /// <summary>Creates the gate.</summary>
    public LocalDisclosureGate(
        IChatClient chatClient,
        IDisclosureLedger ledger,
        IOptions<DisclosureOptions> gateOptions,
        ILogger<LocalDisclosureGate> logger)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(ledger);
        ArgumentNullException.ThrowIfNull(gateOptions);
        ArgumentNullException.ThrowIfNull(logger);

        this.chatClient = chatClient;
        this.ledger = ledger;
        this.gateOptions = gateOptions.Value;
        this.logger = logger;
        this.nameMask = new Regex(
            $@"\b{Regex.Escape(this.gateOptions.OwnerFirstName)}('s)?\b",
            RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DisclosedItem>> ClassifyAsync(
        string question,
        IReadOnlyList<string> context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(context);
        if (context.Count == 0)
        {
            return [];
        }

        var examples = await this.ExamplesAsync(cancellationToken).ConfigureAwait(false);
        var reply = await this.AskAsync(this.BuildPrompt(question, context, examples), cancellationToken)
            .ConfigureAwait(false);
        var decisions = reply is null ? null : Parse(reply, context);
        if (decisions is null)
        {
            var reason = FailureReason(reply);
            this.logger.LogWarning(
                "Disclosure gate {Reason}; withholding all {Count} item(s)", reason, context.Count);
            return [.. context.Select(item => new DisclosedItem(item, Disclosure.Withhold, string.Empty, reason))];
        }

        return decisions;
    }

    /// <summary>
    /// The local model's verdict, or null when it could not be reached. On 2026-09-05 the
    /// sidecar was restarted under a live request and the exception took the whole turn
    /// with it; a gate that cannot judge withholds, it does not crash.
    /// </summary>
    private async Task<string?> AskAsync(string prompt, CancellationToken cancellationToken)
    {
        try
        {
            return await this.chatClient.CompleteAsync(prompt, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            this.logger.LogWarning(exception, "Disclosure gate could not reach the local model");
            return null;
        }
    }

    private const string INSTRUCTIONS =
        """
        You decide what may be sent to an external AI service on the user's behalf.
        The service already knows it is talking to the user and knows his first name, Steve.
        The name by itself never makes an item identifying; the items below call him
        "the user". Judge the rest of the item.
        Items beginning "Earlier —" are what the user and the assistant already said to each
        other in this same chat: pass them unless they name another person or carry specific
        health, financial, or address details.
        For EACH numbered item choose exactly one action:
          pass     - contains nothing that identifies the user or another person beyond his
                     first name
          disguise - the FACT is needed to answer, the identity is not. Rewrite it about
                     an unnamed third party ("a friend", "someone I know") keeping every
                     clinical or technical detail intact: every number, date, exercise or
                     machine name, medication, condition and note stays; only who it is
                     about goes. Prefer this over withhold whenever the fact bears on the
                     question.
          withhold - too personal to send AND not needed to answer this question

        """;

    /// <summary>
    /// The configured examples, then Steve's recorded corrections newest first: what the
    /// gate decided, what he said it should have been, and why. This is the gate learning
    /// his boundaries rather than boundaries in general.
    /// </summary>
    private async Task<List<string>> ExamplesAsync(CancellationToken cancellationToken)
    {
        var examples = new List<string>(this.gateOptions.Examples);
        var corrections = await this.ledger.CorrectionsAsync(EXAMPLE_LIMIT, cancellationToken).ConfigureAwait(false);
        foreach (var decision in corrections)
        {
            var correction = decision.Correction!;
            var why = correction.Note.Length > 0 ? $" because: {correction.Note}" : string.Empty;
            examples.Add(
                $"For \"{decision.Original}\" the gate chose {Word(decision.Disclosure)}; "
                + $"the user says it should have been {Word(correction.Corrected)}{why}");
        }

        return examples;
    }

    private static string Word(Disclosure disclosure)
    {
        return disclosure.ToString().ToLowerInvariant();
    }

    private string BuildPrompt(string question, IReadOnlyList<string> context, IList<string> examples)
    {
        var prompt = new StringBuilder(INSTRUCTIONS);
        prompt.AppendLine("The user's rules:");
        foreach (var rule in this.gateOptions.Rules)
        {
            prompt.Append("- ").AppendLine(rule);
        }

        AppendExamples(prompt, examples);
        prompt.AppendLine();
        prompt.Append("Question being asked: ").AppendLine(question);
        prompt.AppendLine("Items:");
        for (var index = 0; index < context.Count; index++)
        {
            prompt.Append(index + 1).Append(". ").AppendLine(this.Presented(context[index]));
        }

        prompt.AppendLine();
        prompt.AppendLine(
            """Answer with ONLY a JSON array: [{"n":1,"action":"pass|disguise|withhold","text":"...","why":"..."}]""");
        prompt.AppendLine(
            "Only disguise carries text (the rewrite). For pass and withhold omit text entirely. "
            + "Keep why under eight words.");
        return prompt.ToString();
    }

    /// <summary>
    /// The item as the gate reads it: the owner's first name replaced by "the user". The
    /// instructions already said the name alone never identifies, and ADR-0032 measured the
    /// improvement — yet on 2026-09-06 15:28 the gate withheld the gym-machine caption and
    /// two chat lines with "Steve's name identifies", and the log went unwritten again.
    /// A name the model never sees cannot be the reason. What is sent is still the
    /// original: the service knows who it is talking to.
    /// </summary>
    private string Presented(string item)
    {
        return this.nameMask.Replace(item, match => match.Groups[1].Success ? "the user's" : "the user");
    }

    private static void AppendExamples(StringBuilder prompt, IList<string> examples)
    {
        if (examples.Count == 0)
        {
            return;
        }

        prompt.AppendLine();
        prompt.AppendLine("Corrections the user has made before — follow these closely:");
        foreach (var example in examples)
        {
            prompt.Append("- ").AppendLine(example);
        }
    }

    /// <summary>
    /// Why nothing could be read. A reply that opens its array and never closes it ran
    /// into the token ceiling: 2026-09-06 14:55, 36 items, "repeat the item in text",
    /// exactly 1,200 tokens generated and the whole turn's history withheld.
    /// </summary>
    private static string FailureReason(string? reply)
    {
        if (reply is null)
        {
            return "gate unavailable";
        }

        var opened = reply.IndexOf('[', StringComparison.Ordinal);
        return opened >= 0 && reply.LastIndexOf(']') <= opened
            ? $"gate output truncated at {reply.Length} chars"
            : "gate output unreadable";
    }

    private static List<DisclosedItem>? Parse(string reply, IReadOnlyList<string> context)
    {
        var start = reply.IndexOf('[', StringComparison.Ordinal);
        var end = reply.LastIndexOf(']');
        if (start < 0 || end <= start)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(reply[start..(end + 1)]);
            return Read(document, context);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<DisclosedItem>? Read(JsonDocument document, IReadOnlyList<string> context)
    {
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        // Start from "withhold everything" and let the model upgrade individual items.
        // An item the model forgot to mention must not become sendable by omission.
        var decisions = context
            .Select(item => new DisclosedItem(item, Disclosure.Withhold, string.Empty, "not classified"))
            .ToList();

        foreach (var element in document.RootElement.EnumerateArray())
        {
            Apply(element, decisions);
        }

        return decisions;
    }

    private static void Apply(JsonElement element, List<DisclosedItem> decisions)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty("n", out var number)
            || !number.TryGetInt32(out var index)
            || index < 1 || index > decisions.Count
            || !element.TryGetProperty("action", out var action))
        {
            return;
        }

        var original = decisions[index - 1].Original;
        var why = element.TryGetProperty("why", out var reason) ? reason.GetString() ?? string.Empty : string.Empty;
        var text = element.TryGetProperty("text", out var rewritten) ? rewritten.GetString() ?? string.Empty : string.Empty;

        decisions[index - 1] = action.GetString()?.ToLowerInvariant() switch
        {
            "pass" => new DisclosedItem(original, Disclosure.Pass, original, why),
            "disguise" when text.Length > 0 => new DisclosedItem(original, Disclosure.Disguise, text, why),
            _ => new DisclosedItem(original, Disclosure.Withhold, string.Empty, why),
        };
    }
}

/// <summary>Steve's disclosure rules, and the corrections that have taught the gate.</summary>
public sealed class DisclosureOptions
{
    /// <summary>Configuration section.</summary>
    public const string SECTION_NAME = "Disclosure";

    /// <summary>
    /// The owner's first name, which the external service already knows. Masked out of
    /// every item before the gate reads it, so it can never be the reason for a verdict.
    /// </summary>
    public string OwnerFirstName { get; set; } = "Steve";

    /// <summary>
    /// What counts as private. Defaults are a starting point, not a policy — Steve is
    /// expected to edit these, and the gate is only as good as they are.
    /// </summary>
    public IList<string> Rules { get; } =
    [
        "Never send names of people, employers, doctors, or private projects.",
        "Never send addresses, account numbers, credentials, or hostnames.",
        "The user's OWN health facts — conditions, procedures, medications, symptoms, appointments, "
            + "test results — and his workouts PASS as written. He said so himself on 2026-09-06: he "
            + "is not concerned about their privacy, and the service needs them to look after him. "
            + "Never disguise or withhold them; a disguise of his own health fact is a mistake.",
        "Health or personal facts about OTHER people are withheld, not disguised.",
        "Technical facts about code, tools, and public knowledge pass.",
    ];

    /// <summary>
    /// Corrections Steve has made, fed back as examples. This is how the gate learns his
    /// boundaries rather than boundaries in general.
    /// </summary>
    public IList<string> Examples { get; } = [];
}
