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

    /// <summary>Runs one streaming turn, printing tokens as they arrive.</summary>
    public async Task<int> TurnAsync(string request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var stream = await this.turnRunner.BeginStreamingAsync(request, cancellationToken)
            .ConfigureAwait(false);

        Console.WriteLine(
            $"[{stream.Route.Tier} · ~{stream.Context.EstimatedTokens} ctx tokens · "
            + $"{stream.Context.Memories.Count} memories · {stream.Context.Beliefs.Count} beliefs]");
        Console.WriteLine();

        await foreach (var fragment in stream.Tokens.ConfigureAwait(false))
        {
            Console.Write(fragment);
            await Console.Out.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        Console.WriteLine();
        Console.WriteLine();
        Console.WriteLine($"replay: dami trace {stream.TraceId}");
        return 0;
    }
}
