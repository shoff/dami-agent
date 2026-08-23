using Dami.Core.Turns;

namespace Dami.Gateway.Cli;

/// <summary>One interactive turn: the Phase 2 exit shape, from the shell.</summary>
public sealed class ChatCommands
{
    private readonly ITurnRunner turnRunner;

    /// <summary>Creates the commands.</summary>
    public ChatCommands(ITurnRunner turnRunner)
    {
        ArgumentNullException.ThrowIfNull(turnRunner);
        this.turnRunner = turnRunner;
    }

    /// <summary>Runs one turn and prints the answer with its accounting.</summary>
    public async Task<int> TurnAsync(string request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Console.WriteLine("thinking (local model)...");
        var result = await this.turnRunner.RunAsync(request, cancellationToken).ConfigureAwait(false);

        Console.WriteLine();
        Console.WriteLine(result.Answer);
        Console.WriteLine();
        Console.WriteLine(
            $"[{result.Route.Tier} · ~{result.Context.EstimatedTokens} ctx tokens · "
            + $"{result.Context.Memories.Count} memories · {result.Context.Beliefs.Count} beliefs · "
            + $"trace {result.TraceId.ToString("N")[..8]}]");
        Console.WriteLine($"replay: dami trace {result.TraceId}");
        return 0;
    }
}
