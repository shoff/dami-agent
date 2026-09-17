using System.Text;
using Dami.Contracts.Research;

namespace Dami.Gui;

/// <summary>Readable research summaries and portable reports from retained artifacts.</summary>
public static class ResearchPresentation
{
    /// <summary>Describes the saved answer without implying one exists or completed.</summary>
    public static string AnswerLabel(ResearchRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return run.AnswerStatus switch
        {
            ResearchAnswerStatus.Complete => "Saved answer",
            ResearchAnswerStatus.Partial => run.AnswerTruncated ? "Partial answer · archive size limit reached" : "Partial answer · reply interrupted",
            _ => "No saved answer · inspect the collected sources below",
        };
    }

    /// <summary>Copies the evidence and its provenance into a plain-text report.</summary>
    public static string Export(ResearchRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var text = new StringBuilder().AppendLine(run.Question).AppendLine()
            .AppendLine($"Started: {run.StartedAt:O}").AppendLine($"Trace: {run.TraceId:D}")
            .AppendLine($"Outcome: {run.Status} · {run.PagesAttempted} reads · {run.Findings.Count} sources")
            .AppendLine(run.PageLimitReached ? "Page limit reached; more references remained unread." : "")
            .AppendLine(run.Error).AppendLine().AppendLine(AnswerLabel(run)).AppendLine(run.Answer);
        foreach (var finding in run.Findings)
        {
            text.AppendLine().AppendLine($"SOURCE: {finding.Page.Title}").AppendLine(finding.Page.Url.AbsoluteUri)
                .AppendLine("Path: " + string.Join(" → ", finding.Path)).AppendLine(finding.Page.Text);
        }

        foreach (var issue in run.Issues)
        {
            text.AppendLine().AppendLine($"SKIPPED: {issue.Url}").AppendLine(issue.Reason);
        }

        return text.ToString();
    }
}
