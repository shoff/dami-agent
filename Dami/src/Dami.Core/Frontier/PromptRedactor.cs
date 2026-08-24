using System.Text;
using Dami.Contracts.Models;
using Dami.Contracts.Privacy;

namespace Dami.Core.Frontier;

/// <summary>Drafts egress briefs with the local model. The draft is never trusted (C4).</summary>
public sealed class PromptRedactor : IPromptRedactor
{
    private const string INSTRUCTIONS =
        """
        Rewrite the question and its context notes into one self-contained brief for an
        external assistant that knows nothing about the person. Rules:
        - Refer to the person only as "the user". Remove every name of a person, employer,
          family member, doctor, address, account, hostname, or private project unless the
          question is unanswerable without it.
        - Keep technical facts, constraints, and the actual question intact.
        - Output only the brief itself: no preamble, no commentary.
        """;

    private readonly IChatClient chatClient;

    /// <summary>Creates the redactor.</summary>
    public PromptRedactor(IChatClient chatClient)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        this.chatClient = chatClient;
    }

    /// <inheritdoc />
    public async Task<string> DraftAsync(
        string question,
        IReadOnlyList<string> contextLines,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(contextLines);

        var prompt = new StringBuilder(INSTRUCTIONS);
        prompt.Append("\n\nContext notes:\n");
        foreach (var line in contextLines)
        {
            prompt.Append("- ").Append(line).Append('\n');
        }

        prompt.Append("\nQuestion: ").Append(question);

        var draft = await this.chatClient
            .CompleteAsync(prompt.ToString(), cancellationToken).ConfigureAwait(false);
        return draft.Trim();
    }
}
