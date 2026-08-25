namespace Dami.Contracts.Context;

/// <summary>One precise fact a domain holds, offered to retrieval.</summary>
/// <param name="SourceId">The row it came from, so "why is this in my prompt" is answerable.</param>
/// <param name="Text">The fact itself.</param>
/// <param name="AsOf">
/// When it holds, or null when the domain does not know. Null rather than a stand-in date:
/// 25 of 84 health rows carry 1970-01-01 because the column is not nullable and extraction
/// had no date to give, and a fact dated to the epoch tells the frontier something false.
/// </param>
/// <param name="Kind">The domain's own label for it, such as "diagnosis" or "procedure".</param>
public sealed record StructuredFact(Guid SourceId, string Text, DateOnly? AsOf, string Kind);

/// <summary>
/// A domain that can answer "what do you hold that bears on this question".
/// </summary>
/// <remarks>
/// Retrieval over conversation prose finds the passage where something was discussed;
/// a domain holds the fact itself. Asked about a heart condition, the corpus returns
/// several hundred words of a conversation that mentions it, while the health domain
/// holds "Severe aortic stenosis diagnosed, 2026-01-30". Both belong in context, and the
/// second is worth far more per token.
/// </remarks>
public interface IStructuredFactSource
{
    /// <summary>The domain name a query plan names to reach this source, lowercase.</summary>
    string Domain { get; }

    /// <summary>Facts bearing on the request, most relevant first.</summary>
    IAsyncEnumerable<StructuredFact> RelevantAsync(
        string request,
        int limit,
        CancellationToken cancellationToken);
}
