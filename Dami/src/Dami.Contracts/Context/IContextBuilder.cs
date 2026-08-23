namespace Dami.Contracts.Context;

/// <summary>Assembles the turn-specific context for a request, within budget.</summary>
public interface IContextBuilder
{
    /// <summary>Retrieves what is relevant to <paramref name="request"/> and nothing else.</summary>
    Task<AssembledContext> BuildAsync(string request, CancellationToken cancellationToken);
}
