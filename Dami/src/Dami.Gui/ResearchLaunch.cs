namespace Dami.Gui;

/// <summary>A prepared research request, or an explanation of why it cannot be sent.</summary>
public sealed record ResearchLaunchResult(string? Prompt, string Message);

/// <summary>Validates the research form without overwriting another conversation draft.</summary>
public static class ResearchLaunch
{
    /// <summary>Prepares a bounded research request for the existing chat route.</summary>
    public static ResearchLaunchResult Prepare(string seed, string question, ChatDraft current, bool isBusy)
    {
        ArgumentNullException.ThrowIfNull(seed);
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(current);
        if (isBusy)
        {
            return new(null, "A reply is already running. Let it finish, or stop it in Conversation.");
        }

        if (current.Text.Length > 0 || current.Images.Count > 0)
        {
            return new(null, "Your conversation draft is safe. Send or clear it before starting research.");
        }

        var automatic = string.IsNullOrWhiteSpace(seed);
        var validUrl = Uri.TryCreate(seed.Trim(), UriKind.Absolute, out var url)
            && url.Scheme is "http" or "https" && url.UserInfo.Length == 0;
        if (!automatic && !validUrl)
        {
            return new(null, "Enter a full http:// or https:// source URL without embedded credentials.");
        }

        return string.IsNullOrWhiteSpace(question)
            ? new(null, "Add the question you want to investigate.")
            : new((automatic
                ? "Use deep_research once. Choose one to three relevant authoritative public starting URLs yourself "
                    + "and pass them together as seedUrls. Choose before reading web content; do not call search_web first. "
                : $"Use deep_research with seedUrl {url!.AbsoluteUri}. ")
                + $"Investigate this question: {question.Trim()}\n"
                + "Summarize the findings, cite the source URLs, and explain skipped sources, gaps, or limits.",
                "Research requested. Sources appear here when Dami begins reading. Conversation shows the reply; Esc stops it.");
    }
}
