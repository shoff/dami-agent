using System.Text.Json;
using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Models;
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
    ];

    private readonly IImageGenerator images;
    private readonly IPortraitGenerator portraits;
    private readonly IFrontierRecall recall;
    private readonly IFrontierRemember remember;
    private readonly IFrontierScheduling scheduling;
    private readonly IReadOnlyList<FrontierTool> tools;
    private readonly ILogger<FrontierToolBundle> logger;

    /// <summary>Creates the bundle factory.</summary>
    public FrontierToolBundle(
        IImageGenerator images,
        IPortraitGenerator portraits,
        IFrontierRecall recall,
        IFrontierRemember remember,
        IFrontierScheduling scheduling,
        ILogger<FrontierToolBundle> logger)
    {
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(portraits);
        ArgumentNullException.ThrowIfNull(recall);
        ArgumentNullException.ThrowIfNull(remember);
        ArgumentNullException.ThrowIfNull(scheduling);
        ArgumentNullException.ThrowIfNull(logger);
        this.images = images;
        this.portraits = portraits;
        this.recall = recall;
        this.remember = remember;
        this.scheduling = scheduling;
        this.tools = [.. pictureTools, recall.Tool, remember.Tool, scheduling.ScheduleTool, scheduling.ConfirmTool];
        this.logger = logger;
    }

    /// <summary>A fresh bundle for one turn in one channel, collecting the pictures it makes.</summary>
    /// <param name="traceId">The turn's trace.</param>
    /// <param name="channel">Where a scheduled job should deliver, e.g. <c>discord:123</c> or <c>gui</c>.</param>
    public FrontierTurnTools ForTurn(Guid traceId, string channel) => new(this, traceId, channel);

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

        internal FrontierTurnTools(FrontierToolBundle owner, Guid traceId, string channel)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(channel);
            this.owner = owner;
            this.traceId = traceId;
            this.channel = channel;
            this.Toolbox = new FrontierToolbox(owner.tools, this);
        }

        /// <summary>What to offer the frontier.</summary>
        public FrontierToolbox Toolbox { get; }

        /// <summary>Pictures made so far, in the order the frontier asked for them.</summary>
        public IReadOnlyList<GeneratedImage> Pictures => this.pictures;

        /// <inheritdoc />
        public async Task<FrontierToolResult> HandleAsync(FrontierToolCall call, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(call);
            try
            {
                var result = await this.DispatchAsync(call, cancellationToken).ConfigureAwait(false);
                this.owner.logger.LogInformation(
                    "Turn {Trace}: {Tool} {Outcome}", this.traceId, call.Tool, result.Success ? "ok" : "failed");
                return result;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                this.owner.logger.LogWarning(exception, "Turn {Trace}: {Tool} threw", this.traceId, call.Tool);
                return FrontierToolResult.Failed($"{call.Tool} failed: {exception.Message}");
            }
        }

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
                _ => Task.FromResult(FrontierToolResult.Failed($"there is no tool named {call.Tool}")),
            };

        private async Task<FrontierToolResult> PictureAsync(
            Func<CancellationToken, Task<GeneratedImage>> generate, CancellationToken cancellationToken)
        {
            var image = await generate(cancellationToken).ConfigureAwait(false);
            this.pictures.Add(image);
            return FrontierToolResult.Ok($"Done: {image.FileName} is attached to your reply. {ATTACHED}");
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
