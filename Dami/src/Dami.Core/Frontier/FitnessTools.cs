using System.Globalization;
using System.Text.Json;
using Dami.Contracts.Domains;
using Dami.Contracts.Models;
using Microsoft.Extensions.Logging;

namespace Dami.Core.Frontier;

/// <summary>Lets the frontier write Steve's workout into the fitness log (ADR-0030).</summary>
/// <remarks>
/// This is the use the Hermes agent was kept for: a photo of the machine and a line like
/// "4x12 140 lbs RPE 7" becomes rows in the exercise log. The local vision model reads
/// the machine; the frontier reads Steve; this writes. It holds the profile store and
/// nothing that can leave the host.
/// </remarks>
public interface IFrontierFitness
{
    /// <summary>The resistance tool for one turn, naming the exercises the log already uses.</summary>
    Task<FrontierTool> SetsToolAsync(CancellationToken cancellationToken);

    /// <summary>The cardio tool.</summary>
    FrontierTool CardioTool { get; }

    /// <summary>Records sets of one exercise.</summary>
    Task<FrontierToolResult> LogSetsAsync(JsonElement arguments, CancellationToken cancellationToken);

    /// <summary>Records one cardio session.</summary>
    Task<FrontierToolResult> LogCardioAsync(JsonElement arguments, CancellationToken cancellationToken);
}

/// <summary>The fitness log behind two tools.</summary>
public sealed class FitnessTools : IFrontierFitness
{
    /// <summary>The resistance tool's name.</summary>
    public const string LOG_SETS = "log_sets";

    /// <summary>The cardio tool's name.</summary>
    public const string LOG_CARDIO = "log_cardio";

    private const string SOURCE = "claude_chat";

    private const string SETS_DESCRIPTION =
        "Record sets of one exercise in Steve's workout log. Use it whenever he reports lifting — a "
        + "photo of a machine or rack with a line like '4x12 140 lbs RPE 7', or just the words. Take "
        + "the exercise name from the machine or his words. '4x12' means 4 sets of 12 reps; weight is "
        + "in pounds unless he says kg; RPE is 1–10. Log each exercise separately. The result tells "
        + "you what the log already held on that exercise: compare today to it with the numbers, say "
        + "whether it is a best, and if the weight dropped or the RPE climbed, ask how he is feeling. "
        + "Two or three sentences in your own voice, not a receipt.";

    private static readonly TimeSpan knownExercisesFor = TimeSpan.FromMinutes(5);

    private static readonly JsonElement setsSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            exercise = new { type = "string", description = "Exercise or machine name." },
            sets = new { type = "integer", description = "Number of sets." },
            reps = new { type = "integer", description = "Reps per set." },
            weightLbs = new { type = "number", description = "Weight per set in pounds." },
            rpe = new { type = "integer", description = "Rate of perceived exertion, 1–10, if given." },
            muscleGroup = new { type = "string", description = "Primary muscle group, if obvious." },
            equipment = new { type = "string", description = "barbell, dumbbell, cable, machine, bodyweight, kettlebell, band, or other." },
            notes = new { type = "string", description = "Anything else he said." },
        },
        required = new[] { "exercise", "sets", "reps" },
        additionalProperties = false,
    });

    private static readonly string[] modalities =
        ["treadmill", "elliptical", "rowing", "cycling", "walking", "swimming", "sauna", "yard_work", "other_cardio"];

    private readonly IFitnessStore store;
    private readonly TimeProvider clock;
    private readonly ILogger<FitnessTools> logger;
    private (IReadOnlyList<string> Names, DateTimeOffset At)? knownExercises;

    /// <summary>Creates the tools.</summary>
    public FitnessTools(IFitnessStore store, TimeProvider clock, ILogger<FitnessTools> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        this.store = store;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<FrontierTool> SetsToolAsync(CancellationToken cancellationToken)
    {
        var known = await this.KnownExercisesAsync(cancellationToken).ConfigureAwait(false);
        var names = known.Count == 0
            ? string.Empty
            : " Exercises already in his log — when the machine is one of these use this exact spelling, "
              + "so today lands on its history: " + string.Join("; ", known) + ".";
        return new FrontierTool(LOG_SETS, SETS_DESCRIPTION + names, setsSchema);
    }

    /// <inheritdoc />
    public FrontierTool CardioTool { get; } = new(
        LOG_CARDIO,
        "Record one cardio session in Steve's workout log: a treadmill or bike display photo, or his "
        + "words. Read every number the caption gives — minutes, distance, calories, speed, incline, "
        + "heart rate. Modality is one of: treadmill, elliptical, rowing, cycling, walking, swimming, "
        + "sauna, yard_work, other_cardio. Confirm in one short line what you logged.",
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new
            {
                modality = new { type = "string", description = "One of the listed modalities." },
                minutes = new { type = "number", description = "Duration in minutes." },
                distanceMi = new { type = "number", description = "Distance in miles." },
                calories = new { type = "integer", description = "Calories shown." },
                speedMph = new { type = "number", description = "Speed in mph." },
                inclinePct = new { type = "number", description = "Incline percent." },
                hrAvg = new { type = "integer", description = "Average heart rate." },
                hrMax = new { type = "integer", description = "Maximum heart rate." },
                notes = new { type = "string", description = "Anything else he said." },
            },
            required = new[] { "modality" },
            additionalProperties = false,
        }));

    /// <inheritdoc />
    public async Task<FrontierToolResult> LogSetsAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var count = Int(arguments, "sets") ?? throw new ArgumentException("the tool needs 'sets'");
        var reps = Int(arguments, "reps") ?? throw new ArgumentException("the tool needs 'reps'");
        if (count is < 1 or > 50)
        {
            throw new ArgumentException($"{count} sets is not a workout");
        }

        var requested = Text(arguments, "exercise") ?? throw new ArgumentException("the tool needs 'exercise'");
        var known = await this.KnownExercisesAsync(cancellationToken).ConfigureAwait(false);
        var set = new FitnessSetEntry((short)reps, Number(arguments, "weightLbs"), (short?)Int(arguments, "rpe"));
        var entry = new FitnessResistanceEntry(
            this.clock.GetUtcNow(), FitnessHistory.Resolve(known, requested),
            Enumerable.Repeat(set, count).ToList(), SOURCE,
            Text(arguments, "muscleGroup"), Equipment(Text(arguments, "equipment")), Text(arguments, "notes"));
        var id = await this.store.RecordResistanceAsync(entry, cancellationToken).ConfigureAwait(false);
        this.knownExercises = null;
        this.logger.LogInformation("Logged {Sets}x{Reps} {Exercise} as {Id}", count, reps, entry.Exercise, id);
        var weight = set.WeightLbs is { } lbs ? $" at {lbs.ToString("0.#", CultureInfo.InvariantCulture)} lb" : string.Empty;
        var rpe = set.Rpe is { } r ? $", RPE {r}" : string.Empty;
        var history = await this.HistoryAsync(entry.Exercise, id, cancellationToken).ConfigureAwait(false);
        return FrontierToolResult.Ok($"Logged {count}x{reps} {entry.Exercise}{weight}{rpe}.{history}");
    }

    /// <summary>The names the log already uses, read at most every few minutes; empty when it cannot be read.</summary>
    private async Task<IReadOnlyList<string>> KnownExercisesAsync(CancellationToken cancellationToken)
    {
        var now = this.clock.GetUtcNow();
        if (this.knownExercises is { } cached && now - cached.At < knownExercisesFor)
        {
            return cached.Names;
        }

        try
        {
            var snapshot = await this.store.SnapshotAsync(cancellationToken).ConfigureAwait(false);
            var names = FitnessHistory.KnownExercises(snapshot);
            this.knownExercises = (names, now);
            return names;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            this.logger.LogWarning(exception, "Could not read the exercise names from the log");
            return [];
        }
    }

    /// <summary>What the log held on this exercise before today, then what it now notices; a courtesy, never a failure.</summary>
    private async Task<string> HistoryAsync(string exercise, Guid justLogged, CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await this.store.SnapshotAsync(cancellationToken).ConfigureAwait(false);
            var before = FitnessHistory.Describe(snapshot, exercise, justLogged);
            var insights = FitnessInsights.ForExercise(snapshot, exercise, this.clock.GetUtcNow());
            var noticed = insights.Count == 0
                ? string.Empty
                : " Noticed (say this to Steve, with the numbers): " + string.Join(' ', insights.Select(insight => insight.Text));
            return $" {before}{noticed}";
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            this.logger.LogWarning(exception, "Could not read the log back for history");
            return string.Empty;
        }
    }

    /// <inheritdoc />
    public async Task<FrontierToolResult> LogCardioAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var modality = (Text(arguments, "modality") ?? throw new ArgumentException("the tool needs 'modality'"))
            .ToLowerInvariant().Replace(' ', '_');
        if (!modalities.Contains(modality))
        {
            return FrontierToolResult.Failed($"modality must be one of {string.Join(", ", modalities)}");
        }

        var minutes = Number(arguments, "minutes");
        var entry = new FitnessCardioEntry(
            this.clock.GetUtcNow(), modality, SOURCE,
            minutes is { } m ? (int)Math.Round(m * 60) : null,
            Number(arguments, "distanceMi"), Int(arguments, "calories"), Number(arguments, "speedMph"),
            Number(arguments, "inclinePct"), Int(arguments, "hrAvg"), Int(arguments, "hrMax"), Text(arguments, "notes"));
        var id = await this.store.RecordCardioAsync(entry, cancellationToken).ConfigureAwait(false);
        this.logger.LogInformation("Logged {Modality} cardio as {Id}", modality, id);
        var duration = minutes is { } min ? $" for {min.ToString("0.#", CultureInfo.InvariantCulture)} min" : string.Empty;
        var distance = entry.DistanceMi is { } mi ? $", {mi.ToString("0.##", CultureInfo.InvariantCulture)} mi" : string.Empty;
        return FrontierToolResult.Ok($"Logged {modality}{duration}{distance}.");
    }

    private static string? Equipment(string? value)
    {
        var known = new[] { "barbell", "dumbbell", "cable", "machine", "bodyweight", "kettlebell", "band", "other" };
        var normalised = value?.Trim().ToLowerInvariant();
        return normalised is not null && known.Contains(normalised) ? normalised : null;
    }

    private static string? Text(JsonElement arguments, string name) =>
        arguments.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(element.GetString())
            ? element.GetString()!.Trim()
            : null;

    private static int? Int(JsonElement arguments, string name) =>
        arguments.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number
            ? (int)Math.Round(element.GetDouble())
            : null;

    private static decimal? Number(JsonElement arguments, string name) =>
        arguments.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number
            ? element.GetDecimal()
            : null;
}
