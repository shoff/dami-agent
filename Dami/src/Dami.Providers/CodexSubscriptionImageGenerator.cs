using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Models;
using Dami.Contracts.Privacy;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Providers;

/// <summary>Generates images through Codex's authenticated built-in image tool.</summary>
public sealed class CodexSubscriptionImageGenerator : IImageGenerator
{
    private const string ACTOR = "image-codex-subscription";

    private readonly ICodexProcess process;
    private readonly CodexOptions options;
    private readonly IEgressBudget budget;
    private readonly IExecutionEventStore events;
    private readonly TimeProvider clock;
    private readonly ILogger<CodexSubscriptionImageGenerator> logger;

    /// <summary>Creates the subscription image provider.</summary>
    public CodexSubscriptionImageGenerator(
        ICodexProcess process,
        IOptions<CodexOptions> options,
        IEgressBudget budget,
        IExecutionEventStore events,
        TimeProvider clock,
        ILogger<CodexSubscriptionImageGenerator> logger)
    {
        ArgumentNullException.ThrowIfNull(process);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(budget);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        this.process = process;
        this.options = options.Value;
        this.budget = budget;
        this.events = events;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<GeneratedImage> GenerateAsync(
        ImageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        await this.RefuseIfNeededAsync(request, cancellationToken).ConfigureAwait(false);
        Directory.CreateDirectory(this.options.WorkingDirectory);
        var target = Path.Combine(
            this.options.WorkingDirectory, $"dami-generated-{Guid.NewGuid():N}.png");
        var reference = await WriteReferenceAsync(request.Reference, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await this.RunAsync(request, target, reference, cancellationToken).ConfigureAwait(false);
            var bytes = await ReadResultAsync(target, cancellationToken).ConfigureAwait(false);
            await this.EmitAsync(request, ExecutionEventType.EgressCompleted,
                ExecutionStatus.Succeeded, $"{request.Purpose}: {bytes.Length} bytes returned",
                cancellationToken).ConfigureAwait(false);
            return new GeneratedImage(Path.GetFileName(target), bytes, "image/png", request.Prompt);
        }
        finally
        {
            Delete(target);
            Delete(reference);
        }
    }

    private async Task RunAsync(
        ImageRequest request, string target, string? reference, CancellationToken cancellationToken)
    {
        await this.EmitAsync(request, ExecutionEventType.EgressRequested,
            ExecutionStatus.Running, $"{request.Purpose} -> codex subscription image tool",
            cancellationToken).ConfigureAwait(false);
        await this.process.RunAsync(
            this.options.BinaryPath,
            this.Arguments(Prompt(request, target), reference),
            TimeSpan.FromSeconds(this.options.TimeoutSeconds),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task RefuseIfNeededAsync(
        ImageRequest request, CancellationToken cancellationToken)
    {
        var refusal = request.Privacy != PrivacyClass.Egressable
            ? "the image prompt is not Egressable"
            : !this.options.Enabled
                ? "the subscription image provider is not enabled"
                : await this.budget.FindRefusalAsync(cancellationToken).ConfigureAwait(false);
        if (refusal is null)
        {
            return;
        }

        this.logger.LogWarning("Subscription image generation refused: {Reason}", refusal);
        throw new EgressRefusedException(refusal);
    }

    private List<string> Arguments(string prompt, string? reference)
    {
        var arguments = new List<string>
        {
            "exec", "--ephemeral", "--sandbox", "workspace-write",
            "--skip-git-repo-check", "--cd", this.options.WorkingDirectory,
        };
        arguments.Add(prompt);
        if (reference is not null)
        {
            arguments.Add("--image");
            arguments.Add(reference);
        }
        return arguments;
    }

    private static string Prompt(ImageRequest request, string target) => $$"""
        Use the imagegen skill and its built-in image_gen tool to create exactly one PNG.
        Do not use the fallback CLI. This is a project-bound asset, so after generation copy
        the resulting file to the exact path below. {{ReferenceRule(request)}} Inspect the
        generated pixels before finishing.

        {{request.Prompt}}

        SAVE_TO:{{target}}
        Finish only after that exact file exists and is non-empty.
        """;

    /// <summary>What the attached image means: an anchor to preserve, or the picture to change.</summary>
    private static string ReferenceRule(ImageRequest request) =>
        request.EditReference
            ? "The attached image is the source picture. Apply exactly the change described below "
              + "and keep everything else — the person, her identity, the composition, lighting and "
              + "setting — as it is."
            : "If an image is attached, it is the sole identity reference; preserve identity while "
              + "creating a new scene rather than editing the reference pose.";

    private static async Task<string?> WriteReferenceAsync(
        ImageReference? reference, CancellationToken cancellationToken)
    {
        if (reference is null)
        {
            return null;
        }

        var extension = Path.GetExtension(reference.FileName);
        var path = Path.Combine(Path.GetTempPath(), $"dami-reference-{Guid.NewGuid():N}{extension}");
        await File.WriteAllBytesAsync(path, reference.Bytes.ToArray(), cancellationToken)
            .ConfigureAwait(false);
        return path;
    }

    private static async Task<byte[]> ReadResultAsync(
        string target, CancellationToken cancellationToken)
    {
        if (!File.Exists(target))
        {
            throw new InvalidOperationException("The subscription image tool returned no image file.");
        }

        var bytes = await File.ReadAllBytesAsync(target, cancellationToken).ConfigureAwait(false);
        return bytes.Length > 0
            ? bytes
            : throw new InvalidOperationException("The subscription image tool returned an empty image.");
    }

    private static void Delete(string? path)
    {
        if (path is not null)
        {
            File.Delete(path);
        }
    }

    private Task<long> EmitAsync(
        ImageRequest request,
        ExecutionEventType type,
        ExecutionStatus status,
        string label,
        CancellationToken cancellationToken) =>
        this.events.AppendAsync(new ExecutionEvent(
            Guid.NewGuid(), request.TraceId, Guid.NewGuid(), null, request.Origin,
            ACTOR, type, status, this.clock.GetUtcNow(), label), cancellationToken);
}
