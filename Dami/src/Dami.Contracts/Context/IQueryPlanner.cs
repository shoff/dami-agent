namespace Dami.Contracts.Context;

/// <summary>How one request should be searched for, and where.</summary>
/// <param name="Searches">
/// Retrieval queries covering the request. More than one because a question rarely
/// matches the words the corpus used: "my heart condition" has to reach text that says
/// "aortic stenosis", and "what should I ask the surgeon" needs the procedure, the
/// medication, and the risks retrieved separately or the longest passage wins all the slots.
/// </param>
/// <param name="Domains">
/// Domains holding structured facts that bear on the request, by name (for example "health").
/// </param>
/// <param name="Facts">
/// What those domains hold. Planning resolves these before choosing searches, because a
/// vague personal reference cannot be expanded without them: asked to turn "my heart
/// condition" into corpus vocabulary with nothing to go on, the local model returns
/// "heart condition treatment options"; given the domain's rows it returns "severe aortic
/// stenosis" and "mechanical AVR surgery", which is what the notes actually say.
/// </param>
public sealed record QueryPlan(
    IReadOnlyList<string> Searches,
    IReadOnlyList<string> Domains,
    IReadOnlyList<StructuredFact> Facts);

/// <summary>
/// Turns a request into a retrieval plan — the local sidecar's mundane work, done before
/// the frontier is troubled (ADR-0019).
/// </summary>
public interface IQueryPlanner
{
    /// <summary>Plans retrieval for a request. Never throws for a bad plan; degrades to the request itself.</summary>
    Task<QueryPlan> PlanAsync(string request, CancellationToken cancellationToken);
}
