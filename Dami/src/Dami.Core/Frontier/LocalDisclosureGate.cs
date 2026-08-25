using System.Text;
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
    private readonly IChatClient chatClient;
    private readonly DisclosureOptions gateOptions;
    private readonly ILogger<LocalDisclosureGate> logger;

    /// <summary>Creates the gate.</summary>
    public LocalDisclosureGate(
        IChatClient chatClient,
        IOptions<DisclosureOptions> gateOptions,
        ILogger<LocalDisclosureGate> logger)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(gateOptions);
        ArgumentNullException.ThrowIfNull(logger);

        this.chatClient = chatClient;
        this.gateOptions = gateOptions.Value;
        this.logger = logger;
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

        var reply = await this.chatClient
            .CompleteAsync(this.BuildPrompt(question, context), cancellationToken)
            .ConfigureAwait(false);
        var decisions = Parse(reply, context);
        if (decisions is null)
        {
            this.logger.LogWarning(
                "Disclosure gate could not read its own output; withholding all {Count} item(s)",
                context.Count);
            return [.. context.Select(item =>
                new DisclosedItem(item, Disclosure.Withhold, string.Empty, "gate output unreadable"))];
        }

        return decisions;
    }

    private const string INSTRUCTIONS =
        """
        You decide what may be sent to an external AI service on the user's behalf.
        For EACH numbered item choose exactly one action:
          pass     - contains nothing that identifies the user or another person
          disguise - the FACT is needed to answer, the identity is not. Rewrite it about
                     an unnamed third party ("a friend", "someone I know") keeping every
                     clinical or technical detail intact.
          withhold - too personal to send and not needed to answer this question

        """;

    private string BuildPrompt(string question, IReadOnlyList<string> context)
    {
        var prompt = new StringBuilder(INSTRUCTIONS);
        prompt.AppendLine("The user's rules:");
        foreach (var rule in this.gateOptions.Rules)
        {
            prompt.Append("- ").AppendLine(rule);
        }

        AppendExamples(prompt, this.gateOptions.Examples);
        prompt.AppendLine();
        prompt.Append("Question being asked: ").AppendLine(question);
        prompt.AppendLine("Items:");
        for (var index = 0; index < context.Count; index++)
        {
            prompt.Append(index + 1).Append(". ").AppendLine(context[index]);
        }

        prompt.AppendLine();
        prompt.AppendLine(
            """Answer with ONLY a JSON array: [{"n":1,"action":"pass|disguise|withhold","text":"...","why":"..."}]""");
        prompt.AppendLine("For pass, repeat the item in text. For withhold, use an empty text.");
        return prompt.ToString();
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
    /// What counts as private. Defaults are a starting point, not a policy — Steve is
    /// expected to edit these, and the gate is only as good as they are.
    /// </summary>
    public IList<string> Rules { get; } =
    [
        "Never send names of people, employers, doctors, or private projects.",
        "Never send addresses, account numbers, credentials, or hostnames.",
        "Health facts about the user may be DISGUISED when the question needs them; "
            + "the clinical detail matters, the identity does not.",
        "Health or personal facts about OTHER people are withheld, not disguised.",
        "Technical facts about code, tools, and public knowledge pass.",
    ];

    /// <summary>
    /// Corrections Steve has made, fed back as examples. This is how the gate learns his
    /// boundaries rather than boundaries in general.
    /// </summary>
    public IList<string> Examples { get; } = [];
}
