namespace Dami.Contracts.Privacy;

/// <summary>Drafts an egress-candidate brief from a question and its LocalOnly context (C4).</summary>
/// <remarks>
/// The draft is machine-made and therefore untrusted: it stays LocalOnly until a human
/// approves the exact bytes. The redactor's job is a good first draft — self-contained,
/// generic where identity is not essential — not a guarantee. The guarantee is the
/// consent step.
/// </remarks>
public interface IPromptRedactor
{
    /// <summary>Drafts the brief. Output is LocalOnly until approved.</summary>
    Task<string> DraftAsync(
        string question,
        IReadOnlyList<string> contextLines,
        CancellationToken cancellationToken);
}
