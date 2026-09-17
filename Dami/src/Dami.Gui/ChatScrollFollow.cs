namespace Dami.Gui;

/// <summary>Tracks whether the conversation should follow newly arriving text.</summary>
public sealed class ChatScrollFollow
{
    private const double TAIL_TOLERANCE = 32;

    /// <summary>Whether new content should keep the latest message in view.</summary>
    public bool IsFollowing { get; private set; } = true;

    /// <summary>Observes a scroll position, distinguishing a gesture from content reflow.</summary>
    public void Observe(double offset, double extent, double viewport, bool layoutChanged, bool holdPosition = false)
    {
        if (holdPosition)
        {
            this.IsFollowing = false;
        }
        else if (!layoutChanged)
        {
            this.IsFollowing = extent - viewport - offset <= TAIL_TOLERANCE;
        }
    }
    /// <summary>Resumes following after an explicit jump or a new submission.</summary>
    public void Resume() => this.IsFollowing = true;

    /// <summary>Holds an explicitly selected search result while new content arrives.</summary>
    public void Pause() => this.IsFollowing = false;
}
