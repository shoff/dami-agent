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

    /// <summary>Records a lesson.</summary>
    Task<FrontierToolResult> LearnAsync(Guid traceId, string channel, string lesson, CancellationToken cancellationToken);
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
/// </remarks>
public sealed class LessonTool : IFrontierLesson
{
    /// <summary>The tool's name.</summary>
    public const string NAME = "lesson";

    /// <summary>The observation source lessons are recorded under.</summary>
    public const string SOURCE = "lesson";

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
        + "with the reason if he gave one. You will see every lesson on every future turn. Facts "
        + "about Steve go to remember, not here.",
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new { lesson = new { type = "string", description = "The lesson, as one imperative sentence to yourself." } },
            required = new[] { "lesson" },
            additionalProperties = false,
        }));

    /// <inheritdoc />
    public async Task<FrontierToolResult> LearnAsync(
        Guid traceId, string channel, string lesson, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lesson);
        var observation = new Observation(
            Guid.NewGuid(), this.clock.GetUtcNow(), SOURCE, lesson.Trim(),
            new Dictionary<string, string> { ["trace"] = traceId.ToString("N"), ["channel"] = channel });
        await this.corpus.RecordAsync(observation, cancellationToken).ConfigureAwait(false);
        this.logger.LogInformation("Lesson {Id} recorded from {Channel}", observation.ObservationId, channel);
        return FrontierToolResult.Ok("Noted. I will carry that into every conversation from now on.");
    }
}
