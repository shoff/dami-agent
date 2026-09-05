using System.Text.Json;
using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
using Dami.Core.Frontier;
using Microsoft.Extensions.Logging;

namespace Dami.Host.Discord;

/// <summary>The small bundle of tools the frontier gets on a Discord turn (ADR-0030).</summary>
/// <remarks>
/// Two picture tools, Egressable by construction — the only thing that leaves this host
/// is text the frontier itself wrote, and the only thing that comes back is a file name —
/// and one recall tool, which is the only member that reads the profile and therefore
/// goes through the disclosure gate and the egress brief like retrieved context. The
/// bundle is created per turn because it carries the turn's attachments.
/// </remarks>
public sealed class DiscordToolbox
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
    private readonly IDiscordPortraitGenerator portraits;
    private readonly IFrontierRecall recall;
    private readonly IReadOnlyList<FrontierTool> tools;
    private readonly ILogger<DiscordToolbox> logger;

    /// <summary>Creates the bundle factory.</summary>
    public DiscordToolbox(
        IImageGenerator images,
        IDiscordPortraitGenerator portraits,
        IFrontierRecall recall,
        ILogger<DiscordToolbox> logger)
    {
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(portraits);
        ArgumentNullException.ThrowIfNull(recall);
        ArgumentNullException.ThrowIfNull(logger);
        this.images = images;
        this.portraits = portraits;
        this.recall = recall;
        this.tools = [.. pictureTools, recall.Tool];
        this.logger = logger;
    }

    /// <summary>A fresh bundle for one turn, collecting whatever pictures it makes.</summary>
    public DiscordTurnTools ForTurn(Guid traceId) => new(this, traceId);

    private static JsonElement Schema(string argument, string description) =>
        JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new Dictionary<string, object>
            {
                [argument] = new { type = "string", description },
            },
            required = new[] { argument },
            additionalProperties = false,
        });

    /// <summary>One turn's tools and the attachments they produced.</summary>
    public sealed class DiscordTurnTools : IFrontierToolHandler
    {
        private readonly DiscordToolbox owner;
        private readonly Guid traceId;
        private readonly List<OutboundAttachment> attachments = [];

        internal DiscordTurnTools(DiscordToolbox owner, Guid traceId)
        {
            this.owner = owner;
            this.traceId = traceId;
            this.Toolbox = new FrontierToolbox(owner.tools, this);
        }

        /// <summary>What to offer the frontier.</summary>
        public FrontierToolbox Toolbox { get; }

        /// <summary>Pictures made so far, in the order the frontier asked for them.</summary>
        public IReadOnlyList<OutboundAttachment> Attachments => this.attachments;

        /// <inheritdoc />
        public async Task<FrontierToolResult> HandleAsync(
            FrontierToolCall call, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(call);
            try
            {
                if (call.Tool == FrontierRecallTool.NAME)
                {
                    return await this.owner.recall
                        .RecallAsync(this.traceId, Argument(call, "query"), cancellationToken).ConfigureAwait(false);
                }

                var image = await this.GenerateAsync(call, cancellationToken).ConfigureAwait(false);
                if (image is null)
                {
                    return FrontierToolResult.Failed($"there is no tool named {call.Tool}");
                }

                this.attachments.Add(new OutboundAttachment(image.FileName, image.Bytes, image.ContentType));
                this.owner.logger.LogInformation(
                    "Discord turn {Trace}: frontier made {File} with {Tool}", this.traceId, image.FileName, call.Tool);
                return FrontierToolResult.Ok($"Done: {image.FileName} is attached to your reply. {ATTACHED}");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                this.owner.logger.LogWarning(exception, "Discord frontier tool {Tool} failed", call.Tool);
                return FrontierToolResult.Failed($"{call.Tool} failed: {exception.Message}");
            }
        }

        private async Task<GeneratedImage?> GenerateAsync(
            FrontierToolCall call, CancellationToken cancellationToken)
        {
            switch (call.Tool)
            {
                case MAKE_PORTRAIT:
                    return await this.owner.portraits
                        .GenerateAsync(Argument(call, "scene"), cancellationToken).ConfigureAwait(false);
                case MAKE_IMAGE:
                    var request = new ImageRequest(
                        Argument(call, "prompt"), "Discord frontier tool", PrivacyClass.Egressable,
                        this.traceId, ExecutionOrigin.UserTurn);
                    return await this.owner.images.GenerateAsync(request, cancellationToken).ConfigureAwait(false);
                default:
                    return null;
            }
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
