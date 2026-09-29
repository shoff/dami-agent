using System.Globalization;
using System.Text;
using Dami.Contracts.Scheduling;

namespace Dami.Core.Scheduling;

/// <summary>What a Prompt job asks the frontier: its request, what it said last time, and whether silence is allowed.</summary>
/// <remarks>
/// The morning brief was the first thing users of every surveyed agent built and the most
/// abandoned — repetitive, and noisy when nothing had changed (docs/agent-landscape-2026-09.md
/// C1, C2). Hermes carries a cron job's memory between runs; ChatGPT's scheduled tasks alert
/// only on change. Here a job sees its own last outputs, and a change-only job may answer
/// <see cref="NOTHING_NEW"/>, which is then not delivered.
/// </remarks>
public static class JobPrompt
{
    /// <summary>The whole answer of a change-only run that found nothing new.</summary>
    public const string NOTHING_NEW = "NOTHING NEW";

    /// <summary>How many earlier outputs a run sees.</summary>
    public const int REMEMBERED_RUNS = 3;

    private const int REMEMBERED_CHARS = 240;

    /// <summary>The prompt for one run of <paramref name="job"/>, given its latest outputs newest first.</summary>
    public static string Compose(ScheduledJob job, IReadOnlyList<ScheduledJobRun> recent)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(recent);
        var prompt = new StringBuilder($"[scheduled job '{job.Name}'] {job.Payload}");
        if (recent.Count == 0)
        {
            return prompt.ToString();
        }

        prompt.Append("\n\nWhat you said on this job's last runs, newest first — do not repeat it; say what is new since:");
        foreach (var run in recent.Take(REMEMBERED_RUNS))
        {
            var said = run.Output.ReplaceLineEndings(" ").Trim();
            prompt.Append("\n- ")
                .Append(run.RanAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
                .Append(": ")
                .Append(said.Length <= REMEMBERED_CHARS ? said : said[..REMEMBERED_CHARS] + "…");
        }

        if (job.OnlyWhenNew)
        {
            prompt.Append("\n\nIf nothing is new since then, reply with exactly: ").Append(NOTHING_NEW);
        }

        return prompt.ToString();
    }

    /// <summary>Whether <paramref name="answer"/> is nothing but <see cref="NOTHING_NEW"/>.</summary>
    public static bool IsNothingNew(string answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        return string.Equals(answer.Trim().TrimEnd('.').Trim(), NOTHING_NEW, StringComparison.OrdinalIgnoreCase);
    }
}
