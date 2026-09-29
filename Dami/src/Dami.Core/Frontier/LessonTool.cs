using System.Text;
using System.Text.Json;
using Dami.Contracts.Memory;
using Dami.Contracts.Models;
using Microsoft.Extensions.Logging;

namespace Dami.Core.Frontier;

/// <summary>The frontier's door to the lessons ledger.</summary>
public interface IFrontierLesson
{
    /// <summary>The tool as offered to the frontier.</summary>
    FrontierTool Tool { get; }

    /// <summary>
    /// Records a lesson, provided <paramref name="quote"/> is something Steve said in
    /// <paramref name="stevesWords"/>, the message this turn answers; null when no message
    /// of his started the turn.
    /// </summary>
    Task<FrontierToolResult> LearnAsync(
        Guid traceId, string channel, string lesson, string quote, string? stevesWords, CancellationToken cancellationToken);
}

/// <summary>
/// A correction Steve gives becomes a standing lesson: one observation, source
/// <c>lesson</c>, that <see cref="StandingLessons"/> hands the frontier on every turn.
/// </summary>
/// <remarks>
/// The addon users install first in every ecosystem surveyed on 2026-09-16 is the one
/// that captures corrections (ClawHub's <c>self-improving-agent</c>, Hermes's
/// <c>/learn</c>). Dami recorded corrections in three ledgers and fed none of them back;
/// the "I am not concerned about privacy of my workouts" correction of 2026-09-06 had to
/// become an ADR by hand. Lessons are about how Dami should behave, not facts about Steve
/// — those are <see cref="RememberTool"/>'s.
/// <para>
/// Guarded against the failures Hermes users report most (2026-09-29): a lesson must quote
/// Steve's own words from the message being answered, so Dami cannot learn from grading
/// herself, and nothing is learned on a turn he did not start; a lesson is one sentence,
/// so twenty of them stay a small prompt. Lessons are append-only observations, so none
/// can overwrite another, and <see cref="StandingLessons"/> lets one lapse that has not
/// been said again in ninety days.
/// </para>
/// </remarks>
public sealed class LessonTool : IFrontierLesson
{
    /// <summary>The tool's name.</summary>
    public const string NAME = "lesson";

    /// <summary>The observation source lessons are recorded under.</summary>
    public const string SOURCE = "lesson";

    private const int MAXIMUM_LESSON_CHARS = 300;

    private readonly IObservationCorpus corpus;
    private readonly TimeProvider clock;
    private readonly ILogger<LessonTool> logger;

    /// <summary>Creates the tool.</summary>
    public LessonTool(IObservationCorpus corpus, TimeProvider clock, ILogger<LessonTool> logger)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        this.corpus = corpus;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public FrontierTool Tool { get; } = new(
        NAME,
        "Record a standing lesson about how you should behave, when Steve corrects you or "
        + "tells you how he wants things done (\"don't disguise my workouts\", \"stop apologising\", "
        + "\"keep weather to one line\"). Write it as one imperative sentence addressed to yourself, "
        + "with the reason if he gave one, and quote the words of his current message it comes from. "
        + "Only Steve's words make a lesson: never record one from your own judgment of how a turn "
        + "went. You will see every lesson on every future turn. Facts about Steve go to remember, not here.",
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                lesson = new { type = "string", description = "The lesson, as one imperative sentence to yourself." },
                quote = new { type = "string", description = "Steve's exact words from his current message that the lesson comes from." },
            },
            required = new[] { "lesson", "quote" },
            additionalProperties = false,
        }));

    /// <inheritdoc />
    public async Task<FrontierToolResult> LearnAsync(
        Guid traceId, string channel, string lesson, string quote, string? stevesWords,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lesson);
        ArgumentNullException.ThrowIfNull(quote);
        var refusal = Refusal(lesson.Trim(), quote, stevesWords);
        if (refusal is not null)
        {
            this.logger.LogInformation("Lesson refused on {Channel}: {Reason}", channel, refusal);
            return FrontierToolResult.Failed(refusal);
        }

        var observation = new Observation(
            Guid.NewGuid(), this.clock.GetUtcNow(), SOURCE, lesson.Trim(),
            new Dictionary<string, string>
            {
                ["trace"] = traceId.ToString("N"), ["channel"] = channel, ["quote"] = quote.Trim(),
            });
        await this.corpus.RecordAsync(observation, cancellationToken).ConfigureAwait(false);
        this.logger.LogInformation("Lesson {Id} recorded from {Channel}", observation.ObservationId, channel);
        return FrontierToolResult.Ok("Noted. I will carry that into every conversation from now on.");
    }

    private static string? Refusal(string lesson, string quote, string? stevesWords)
    {
        if (stevesWords is null)
        {
            return "no lesson recorded: Steve did not start this turn, so there are no words of his to learn from";
        }

        if (lesson.Length > MAXIMUM_LESSON_CHARS)
        {
            return $"no lesson recorded: a lesson is one sentence of at most {MAXIMUM_LESSON_CHARS} characters";
        }

        var said = Normalized(quote);
        return said.Length > 0 && Normalized(stevesWords).Contains(said, StringComparison.Ordinal)
            ? null
            : "no lesson recorded: the quote is not in Steve's message. A lesson comes from his words, "
                + "never from your own judgment of the turn; quote him exactly or record nothing";
    }

    /// <summary>Lower case, apostrophes dropped, every other run of non-letters one space.</summary>
    private static string Normalized(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
            else if (character is not ('\'' or '\u2019') && builder.Length > 0 && builder[^1] != ' ')
            {
                builder.Append(' ');
            }
        }

        return builder.ToString().Trim();
    }
}
