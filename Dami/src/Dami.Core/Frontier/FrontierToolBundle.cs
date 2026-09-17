using System.Globalization;
using System.Text.Json;
using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Gallery;
using Dami.Contracts.Models;
using Dami.Core.Gallery;
using Microsoft.Extensions.Logging;

namespace Dami.Core.Frontier;

/// <summary>The small bundle of tools a frontier turn gets (ADR-0030), channel-agnostic.</summary>
/// <remarks>
/// Pictures, memory in and out, and scheduling. The picture tools are Egressable by
/// construction: text the frontier wrote leaves, a file name comes back. <c>recall</c>
/// is the only member that reads the profile and goes through the disclosure gate. The
/// bundle is created per turn because it carries the turn's pictures and the channel a
/// scheduled job should deliver to.
/// </remarks>
public sealed class FrontierToolBundle
{
    /// <summary>The tool that draws Dami herself.</summary>
    public const string MAKE_PORTRAIT = "make_portrait";

    /// <summary>The tool that draws anything else.</summary>
    public const string MAKE_IMAGE = "make_image";

    /// <summary>The tool that searches the Gallery by what is in the pictures.</summary>
    public const string FIND_PICTURES = "find_pictures";

    /// <summary>The tool that attaches an existing Gallery picture.</summary>
    public const string SHOW_PICTURE = "show_picture";

    /// <summary>The tool that changes an existing Gallery picture into a new one.</summary>
    public const string RETOUCH_PICTURE = "retouch_picture";

    private const int FIND_LIMIT = 6;

    private const string ATTACHED =
        "The picture is attached to your reply automatically. Describe it in one short "
        + "line if you like; do not write a file name, path, or link.";

    private static readonly IReadOnlyList<FrontierTool> pictureTools =
    [
        new(
            MAKE_PORTRAIT,
            "Create a new photo of yourself (Dami) in a described scene, identity preserved. "
            + "Use it whenever Steve asks to see you, a picture/photo/selfie of you, or what you "
            + "are doing. " + ATTACHED,
            Schema("scene", "The scene, pose, mood, setting and wardrobe, in one or two sentences.")),
        new(
            MAKE_IMAGE,
            "Create a picture of anything that is not you: an object, a place, a diagram, an "
            + "illustration. " + ATTACHED,
            Schema("prompt", "What to draw, in one or two sentences.")),
        new(
            FIND_PICTURES,
            "Search the Gallery of existing pictures of you by what is in them: setting, mood, "
            + "wardrobe, activity, time of day, or when they were made. Use it before making a new "
            + "picture when Steve asks for one that may already exist (\"the balcony one\", "
            + "\"yesterday's\", \"that red dress\"). Returns file names with dates and captions.",
            Schema("query", "A short search phrase describing the picture.")),
        new(
            SHOW_PICTURE,
            "Attach an existing Gallery picture to your reply by its exact file name from "
            + "find_pictures. " + ATTACHED,
            Schema("fileName", "The file name exactly as find_pictures returned it.")),
        new(
            RETOUCH_PICTURE,
            "Change an existing Gallery picture of you and attach the result as a new picture: "
            + "\"make it golden hour\", \"same but in the workshop\", \"give me a red dress\". Use the "
            + "exact file name from find_pictures. " + ATTACHED,
            JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    fileName = new { type = "string", description = "The file name exactly as find_pictures returned it." },
                    instruction = new { type = "string", description = "The change to make, in one sentence." },
                },
                required = new[] { "fileName", "instruction" },
                additionalProperties = false,
            })),
    ];

    private readonly IImageGenerator images;
    private readonly IPortraitGenerator portraits;
    private readonly IFrontierRecall recall;
    private readonly IFrontierRemember remember;
    private readonly IFrontierScheduling scheduling;
    private readonly IGallerySearch gallery;
    private readonly IGalleryPictures pictures;
    private readonly IFrontierFitness fitness;
    private readonly IFrontierResearch research;
    private readonly IFrontierToday today;
    private readonly IFrontierCode code;
    private readonly ILogger<FrontierToolBundle> logger;

    /// <summary>Creates the bundle factory.</summary>
    public FrontierToolBundle(
        IImageGenerator images,
        IPortraitGenerator portraits,
        IFrontierRecall recall,
        IFrontierRemember remember,
        IFrontierScheduling scheduling,
        IGallerySearch gallery,
        IGalleryPictures pictures,
        IFrontierFitness fitness,
        IFrontierResearch research,
        IFrontierToday today,
        IFrontierCode code,
        ILogger<FrontierToolBundle> logger)
    {
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(portraits);
        ArgumentNullException.ThrowIfNull(recall);
        ArgumentNullException.ThrowIfNull(remember);
        ArgumentNullException.ThrowIfNull(scheduling);
        ArgumentNullException.ThrowIfNull(gallery);
        ArgumentNullException.ThrowIfNull(pictures);
        ArgumentNullException.ThrowIfNull(fitness);
        ArgumentNullException.ThrowIfNull(research);
        ArgumentNullException.ThrowIfNull(today);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(logger);
        this.images = images;
        this.portraits = portraits;
        this.recall = recall;
        this.remember = remember;
        this.scheduling = scheduling;
        this.gallery = gallery;
        this.pictures = pictures;
        this.fitness = fitness;
        this.research = research;
        this.today = today;
        this.code = code;
        this.logger = logger;
    }

    /// <summary>A fresh bundle for one turn in one channel, collecting the pictures it makes.</summary>
    /// <param name="traceId">The turn's trace.</param>
    /// <param name="channel">Where a scheduled job should deliver, e.g. <c>discord:123</c> or <c>gui</c>.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <remarks>
    /// Assembled per turn because one tool is not static: <c>log_sets</c> carries the exercise
    /// names the log already uses, so a machine photo lands on its own history instead of
    /// minting "biceps curl (Hammer Strength)" beside "biceps curl machine" (2026-09-06).
    /// </remarks>
    public async Task<FrontierTurnTools> ForTurnAsync(Guid traceId, string channel, CancellationToken cancellationToken)
    {
        var sets = await this.fitness.SetsToolAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<FrontierTool> tools =
        [
            .. pictureTools, this.recall.Tool, this.remember.Tool, this.scheduling.ScheduleTool, this.scheduling.ConfirmTool,
            sets, this.fitness.CardioTool, this.research.SearchTool, this.research.ReadTool, this.research.DeepTool, this.today.Tool,
            .. this.code.Tools, // ADR-0036: empty unless CodeWork:Enabled
        ];
        return new FrontierTurnTools(this, traceId, channel, tools);
    }

    private static JsonElement Schema(string argument, string description) =>
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new Dictionary<string, object> { [argument] = new { type = "string", description } },
            required = new[] { argument },
            additionalProperties = false,
        });

    /// <summary>One turn's tools and the pictures they produced.</summary>
    public sealed class FrontierTurnTools : IFrontierToolHandler
    {
        private readonly FrontierToolBundle owner;
        private readonly Guid traceId;
        private readonly string channel;
        private readonly List<GeneratedImage> pictures = [];
        private bool untrustedResearchSeen;

        internal FrontierTurnTools(FrontierToolBundle owner, Guid traceId, string channel, IReadOnlyList<FrontierTool> tools)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(channel);
            this.owner = owner;
            this.traceId = traceId;
            this.channel = channel;
            this.Toolbox = new FrontierToolbox(tools, this);
        }

        /// <summary>What to offer the frontier.</summary>
        public FrontierToolbox Toolbox { get; }

        /// <summary>Pictures made so far, in the order the frontier asked for them.</summary>
        public IReadOnlyList<GeneratedImage> Pictures => this.pictures;

        /// <inheritdoc />
        public async Task<FrontierToolResult> HandleAsync(FrontierToolCall call, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(call);
            if (this.untrustedResearchSeen)
            {
                return FrontierToolResult.Failed(
                    $"{call.Tool} is unavailable after untrusted research entered this turn; no further tools may run until a new turn");
            }

            try
            {
                var result = await this.DispatchAsync(call, cancellationToken).ConfigureAwait(false);
                this.untrustedResearchSeen |= result.Success && IsResearch(call.Tool);
                this.owner.logger.LogInformation(
                    "Turn {Trace}: {Tool} {Outcome} — {Result}", this.traceId, call.Tool,
                    result.Success ? "ok" : "failed", result.Text.Length > 400 ? result.Text[..400] + "…" : result.Text);
                return result;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                this.owner.logger.LogWarning(exception, "Turn {Trace}: {Tool} threw", this.traceId, call.Tool);
                return FrontierToolResult.Failed($"{call.Tool} failed: {exception.Message}");
            }
        }

        private static bool IsResearch(string tool) => tool is
            ResearchTools.SEARCH_WEB or ResearchTools.READ_PAGE or ResearchTools.DEEP_RESEARCH;

        private Task<FrontierToolResult> DispatchAsync(FrontierToolCall call, CancellationToken cancellationToken) =>
            call.Tool switch
            {
                MAKE_PORTRAIT => this.PictureAsync(
                    token => this.owner.portraits.GenerateAsync(Argument(call, "scene"), token), cancellationToken),
                MAKE_IMAGE => this.PictureAsync(
                    token => this.owner.images.GenerateAsync(
                        new ImageRequest(
                            Argument(call, "prompt"), "frontier tool", PrivacyClass.Egressable,
                            this.traceId, ExecutionOrigin.UserTurn),
                        token),
                    cancellationToken),
                FrontierRecallTool.NAME => this.owner.recall.RecallAsync(
                    this.traceId, Argument(call, "query"), cancellationToken),
                RememberTool.NAME => this.owner.remember.RememberAsync(
                    this.traceId, this.channel, Argument(call, "note"), cancellationToken),
                ScheduleTools.SCHEDULE => this.owner.scheduling.ScheduleAsync(
                    this.channel, call.Arguments, cancellationToken),
                ScheduleTools.CONFIRM => this.owner.scheduling.ConfirmAsync(
                    Argument(call, "draftId"), cancellationToken),
                TodayTool.NAME => this.owner.today.TodayAsync(this.traceId, cancellationToken),
                ResearchTools.SEARCH_WEB => this.owner.research.SearchAsync(this.traceId, Argument(call, "query"), cancellationToken),
                ResearchTools.READ_PAGE => this.owner.research.ReadAsync(this.traceId, Argument(call, "url"), cancellationToken),
                ResearchTools.DEEP_RESEARCH => this.ResearchAsync(call, cancellationToken),
                FitnessTools.LOG_SETS => this.owner.fitness.LogSetsAsync(call.Arguments, cancellationToken),
                FitnessTools.LOG_CARDIO => this.owner.fitness.LogCardioAsync(call.Arguments, cancellationToken),
                CodeTools.CHANGE_CODE => this.owner.code.ChangeAsync(this.traceId, Argument(call, "task"), cancellationToken),
                CodeTools.EXPLAIN_CODE => this.owner.code.ExplainAsync(this.traceId, Argument(call, "question"), cancellationToken),
                CodeTools.LIST_CODE_CHANGES => this.owner.code.ListAsync(cancellationToken),
                FIND_PICTURES => this.FindAsync(Argument(call, "query"), cancellationToken),
                SHOW_PICTURE => this.ShowAsync(Argument(call, "fileName"), cancellationToken),
                RETOUCH_PICTURE => this.PictureAsync(
                    token => this.owner.portraits.EditAsync(Argument(call, "fileName").Trim(), Argument(call, "instruction"), token),
                    cancellationToken),
                _ => Task.FromResult(FrontierToolResult.Failed($"there is no tool named {call.Tool}")),
            };

        private Task<FrontierToolResult> ResearchAsync(FrontierToolCall call, CancellationToken token) =>
            call.Arguments.TryGetProperty("seedUrls", out var urls) && urls.ValueKind == JsonValueKind.Array
                ? this.owner.research.DeepAsync(this.traceId,
                    urls.EnumerateArray().Select(url => url.GetString() ?? "").ToArray(), Argument(call, "question"), token)
                : this.owner.research.DeepAsync(this.traceId, Argument(call, "seedUrl"), Argument(call, "question"), token);

        private async Task<FrontierToolResult> PictureAsync(
            Func<CancellationToken, Task<GeneratedImage>> generate, CancellationToken cancellationToken)
        {
            var image = await generate(cancellationToken).ConfigureAwait(false);
            this.pictures.Add(image);
            return FrontierToolResult.Ok($"Done: {image.FileName} is attached to your reply. {ATTACHED}");
        }

        private async Task<FrontierToolResult> FindAsync(string query, CancellationToken cancellationToken)
        {
            var hits = await this.owner.gallery.SearchAsync(query, FIND_LIMIT, cancellationToken).ConfigureAwait(false);
            if (hits.Count == 0)
            {
                return FrontierToolResult.Ok("No Gallery picture matches that.");
            }

            var lines = hits.Select(hit =>
                $"{hit.Entry.FileName} | {hit.Entry.CreatedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)} | "
                + (hit.Entry.Caption ?? hit.Entry.Prompt));
            return FrontierToolResult.Ok(
                "Matches, best first (file | made | what is in it):\n" + string.Join('\n', lines)
                + $"\nCall {SHOW_PICTURE} with a file name to attach one.");
        }

        private async Task<FrontierToolResult> ShowAsync(string fileName, CancellationToken cancellationToken)
        {
            var picture = await this.owner.pictures.LoadAsync(fileName.Trim(), cancellationToken).ConfigureAwait(false);
            if (picture is null)
            {
                return FrontierToolResult.Failed($"the Gallery has no picture named {fileName}");
            }

            this.pictures.Add(picture);
            return FrontierToolResult.Ok($"Done: {picture.FileName} is attached to your reply. {ATTACHED}");
        }

        private static string Argument(FrontierToolCall call, string name)
        {
            var value = call.Arguments.TryGetProperty(name, out var element) ? element.GetString() : null;
            return string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException($"the tool needs a non-empty '{name}'")
                : value;
        }
    }
}
