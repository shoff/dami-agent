namespace Dami.Contracts.Proactive;

/// <summary>One recorded reaction: what was surfaced, and what Steve said about it.</summary>
/// <remarks>The taste model's training pair (D-019).</remarks>
public sealed record SurfacingReaction
{
    /// <summary>Creates a reaction.</summary>
    public SurfacingReaction(string title, string feedback)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(feedback);

        this.Title = title;
        this.Feedback = feedback;
    }

    /// <summary>The surfaced title the reaction was about.</summary>
    public string Title { get; }

    /// <summary>The reaction as recorded — "good: …", "bad: …", "meh".</summary>
    public string Feedback { get; }

    /// <summary>True when the reaction starts with "good".</summary>
    public bool IsPositive => this.Feedback.StartsWith("good", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the reaction starts with "bad".</summary>
    public bool IsNegative => this.Feedback.StartsWith("bad", StringComparison.OrdinalIgnoreCase);
}
