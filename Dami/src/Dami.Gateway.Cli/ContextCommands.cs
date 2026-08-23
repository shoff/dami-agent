using Dami.Contracts.Context;

namespace Dami.Gateway.Cli;

/// <summary>Shows exactly what would enter a prompt for a request — the budget, visible.</summary>
public sealed class ContextCommands
{
    private readonly IContextBuilder contextBuilder;

    /// <summary>Creates the commands.</summary>
    public ContextCommands(IContextBuilder contextBuilder)
    {
        ArgumentNullException.ThrowIfNull(contextBuilder);
        this.contextBuilder = contextBuilder;
    }

    /// <summary>Assembles and prints the context for a hypothetical request.</summary>
    public async Task<int> ShowAsync(string request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var context = await this.contextBuilder.BuildAsync(request, cancellationToken).ConfigureAwait(false);

        Console.WriteLine($"~{context.EstimatedTokens} tokens  ({context.Beliefs.Count} beliefs, {context.Memories.Count} memories)");
        Console.WriteLine();

        foreach (var belief in context.Beliefs)
        {
            Console.WriteLine($"belief  {belief.AsOf:yyyy-MM-dd}  {belief.Content}");
        }

        foreach (var memory in context.Memories)
        {
            Console.WriteLine($"memory  {memory.AsOf:yyyy-MM-dd}  {Shorten(memory.Content)}");
        }

        return 0;
    }

    private static string Shorten(string content)
    {
        var flat = content.ReplaceLineEndings(" ");
        return flat.Length <= 130 ? flat : flat[..130] + "…";
    }
}
