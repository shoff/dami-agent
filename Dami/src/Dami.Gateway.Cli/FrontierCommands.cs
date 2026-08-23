using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Models;

namespace Dami.Gateway.Cli;

/// <summary>A frontier question through the subscription (ADR-0011).</summary>
/// <remarks>
/// Deliberately context-free: per ADR-0010 §5, memory-derived content is LocalOnly and
/// no redaction step exists yet, so what crosses the boundary is the bare question and
/// nothing else. `dami ask`/`dami chat` remain the memory-aware, fully local paths.
/// </remarks>
public sealed class FrontierCommands
{
    private readonly IFrontierChat frontierChat;

    /// <summary>Creates the commands.</summary>
    public FrontierCommands(IFrontierChat frontierChat)
    {
        ArgumentNullException.ThrowIfNull(frontierChat);
        this.frontierChat = frontierChat;
    }

    /// <summary>Sends one bare question to the frontier and prints the answer.</summary>
    public async Task<int> AskAsync(string question, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(question);

        var traceId = Guid.NewGuid();
        Console.WriteLine("asking the frontier (subscription, no API billing)...");

        var prompt = new FrontierPrompt(
            question, "dami frontier question", PrivacyClass.Egressable,
            traceId, ExecutionOrigin.UserTurn);

        var answer = await this.frontierChat.CompleteAsync(prompt, cancellationToken).ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine(answer);
        Console.WriteLine();
        Console.WriteLine($"[frontier via codex subscription · no memories sent · trace {traceId.ToString("N")[..8]}]");
        return 0;
    }
}
