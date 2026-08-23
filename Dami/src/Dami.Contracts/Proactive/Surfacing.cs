namespace Dami.Contracts.Proactive;

/// <summary>Something Dami decided was worth Steve's attention, unprompted.</summary>
/// <remarks>
/// Deliberately a different type from <see cref="Memory.Conclusion"/>, and the
/// separation is the point (D-021): most passes conclude things and surface nothing. A
/// muse that speaks constantly is an infestation; one good observation is worth more
/// than a feed.
/// </remarks>
public sealed record Surfacing
{
    /// <summary>Creates a surfacing.</summary>
    public Surfacing(
        Guid surfacingId,
        string serviceName,
        string title,
        string body,
        double confidence,
        DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(serviceName);
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(body);

        if (confidence is < 0.0 or > 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(confidence), confidence, "Confidence is a probability in [0, 1].");
        }

        this.SurfacingId = surfacingId;
        this.ServiceName = serviceName;
        this.Title = title;
        this.Body = body;
        this.Confidence = confidence;
        this.CreatedAt = createdAt;
    }

    /// <summary>Identity.</summary>
    public Guid SurfacingId { get; }

    /// <summary>The proactive service that produced it.</summary>
    public string ServiceName { get; }

    /// <summary>One line, as it would appear in the queue.</summary>
    public string Title { get; }

    /// <summary>The observation itself, in full.</summary>
    public string Body { get; }

    /// <summary>How confident the service is that this was worth saying.</summary>
    public double Confidence { get; }

    /// <summary>When the service produced it.</summary>
    public DateTimeOffset CreatedAt { get; }
}
