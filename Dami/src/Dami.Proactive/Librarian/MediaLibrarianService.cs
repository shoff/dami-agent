using System.Text.Json;
using Dami.Contracts.Approvals;
using Dami.Contracts.Models;
using Dami.Contracts.Proactive;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Proactive.Librarian;

/// <summary>Propose-only file organization (D-020, §6.2).</summary>
/// <remarks>
/// This is the one background capability that could destroy something irreversibly,
/// which is why it gets the strictest treatment in the system: the service READS the
/// tree and WRITES exactly one manifest file per pass, into its own manifest directory.
/// It holds no move, rename, or delete code at all — not gated, not approval-wrapped,
/// absent. Executing an approved manifest is a different component's job, in a later
/// phase, behind the approval contract.
///
/// v1 groups by file kind and modification year. Vision-based categorization is
/// Phase 6; the manifest format is what survives that upgrade.
/// </remarks>
public sealed class MediaLibrarianService : IProactiveService
{
    private static readonly JsonSerializerOptions manifestFormat = new() { WriteIndented = true };

    private static readonly IReadOnlyDictionary<string, string> kindsByExtension =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = "photos",
            [".jpeg"] = "photos",
            [".png"] = "photos",
            [".heic"] = "photos",
            [".gif"] = "photos",
            [".webp"] = "photos",
            [".mp4"] = "video",
            [".mov"] = "video",
            [".mkv"] = "video",
            [".mp3"] = "audio",
            [".flac"] = "audio",
            [".wav"] = "audio",
            [".pdf"] = "documents",
            [".epub"] = "documents",
            [".stl"] = "models",
            [".3mf"] = "models",
            [".gcode"] = "models",
        };

    private readonly IVisionClient visionClient;
    private readonly IApprovalService approvalService;
    private readonly MediaLibrarianOptions librarianOptions;
    private readonly TimeProvider clock;
    private readonly ILogger<MediaLibrarianService> logger;

    /// <summary>Creates the service.</summary>
    public MediaLibrarianService(
        IVisionClient visionClient,
        IApprovalService approvalService,
        IOptions<MediaLibrarianOptions> librarianOptions,
        TimeProvider clock,
        ILogger<MediaLibrarianService> logger)
    {
        ArgumentNullException.ThrowIfNull(visionClient);
        ArgumentNullException.ThrowIfNull(approvalService);
        ArgumentNullException.ThrowIfNull(librarianOptions);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);

        this.visionClient = visionClient;
        this.approvalService = approvalService;
        this.librarianOptions = librarianOptions.Value;
        this.clock = clock;
        this.logger = logger;
    }

    /// <inheritdoc />
    public string ServiceName => "media-librarian";

    /// <inheritdoc />
    public ProactiveCadence Cadence => ProactiveCadence.Weekly;

    /// <inheritdoc />
    public async Task<ProactiveResult> RunPassAsync(
        ProactiveContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var proposals = this.Survey(cancellationToken);

        if (proposals.Count < this.librarianOptions.MinimumLooseFiles)
        {
            this.logger.LogInformation(
                "Media librarian: {Count} loose file(s); below the floor, staying quiet", proposals.Count);
            return ProactiveResult.quiet;
        }

        await this.EnrichWithVisionAsync(proposals, cancellationToken).ConfigureAwait(false);
        return await this.ProposeAsync(context, proposals, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ProactiveResult> ProposeAsync(
        ProactiveContext context,
        List<MoveProposal> proposals,
        CancellationToken cancellationToken)
    {
        var manifestPath = await this.WriteManifestAsync(proposals, cancellationToken).ConfigureAwait(false);

        // The manifest becomes a first-class approval request (charter §10.2). Until a
        // human approves it, the executor will not touch it; if denied, it never runs.
        var approvalId = Guid.NewGuid();
        await this.approvalService.RequestAsync(
            new ApprovalRequest(
                approvalId, context.TraceId, this.ServiceName,
                $"Execute the proposed organization of {proposals.Count} file(s)",
                "filesystem", manifestPath, this.clock.GetUtcNow(),
                origin: Dami.Contracts.Events.ExecutionOrigin.ScheduledService),
            cancellationToken).ConfigureAwait(false);

        var surfacing = new Surfacing(
            Guid.NewGuid(),
            this.ServiceName,
            $"Proposed organization for {proposals.Count} loose file(s)",
            $"Manifest at {manifestPath}. Nothing moves without approval: "
            + $"dami approve {approvalId.ToString("N")[..8]} (or deny).",
            0.9,
            this.clock.GetUtcNow());

        return new ProactiveResult([], [surfacing], ProactiveStatus.Completed);
    }

    private List<MoveProposal> Survey(CancellationToken cancellationToken)
    {
        var proposals = new List<MoveProposal>();

        foreach (var root in this.librarianOptions.RootPaths)
        {
            if (!Directory.Exists(root))
            {
                this.logger.LogWarning("Media librarian: root {Root} does not exist; skipping", root);
                continue;
            }

            this.SurveyRoot(root, proposals, cancellationToken);
        }

        return proposals;
    }

    private void SurveyRoot(string root, List<MoveProposal> proposals, CancellationToken cancellationToken)
    {
        // Top level only, deliberately: a file already inside a subdirectory has been
        // organized by someone, and second-guessing that is how trust is lost.
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System | FileAttributes.ReparsePoint,
        };

        foreach (var path in Directory.EnumerateFiles(root, "*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (proposals.Count >= this.librarianOptions.MaxFilesPerPass)
            {
                this.logger.LogWarning(
                    "Media librarian: survey capped at {Cap} files", this.librarianOptions.MaxFilesPerPass);
                return;
            }

            var proposal = Propose(root, path);
            if (proposal is not null)
            {
                proposals.Add(proposal);
            }
        }
    }

    private static MoveProposal? Propose(string root, string path)
    {
        var extension = Path.GetExtension(path);
        if (!kindsByExtension.TryGetValue(extension, out var kind))
        {
            return null;
        }

        var modified = File.GetLastWriteTimeUtc(path);
        var proposedDirectory = Path.Combine(root, kind, modified.ToString("yyyy-MM"));

        return new MoveProposal(
            path,
            Path.Combine(proposedDirectory, Path.GetFileName(path)),
            $"{kind}, last modified {modified:yyyy-MM}");
    }

    /// <summary>Adds a local caption to image proposals when vision is enabled.</summary>
    /// <remarks>
    /// Enrichment only: vision refines the Reason line and never changes the proposed
    /// path in v1, so a vision failure degrades to the extension/date proposal rather
    /// than losing the file from the manifest. The image bytes go to loopback and
    /// nowhere else.
    /// </remarks>
    private async Task EnrichWithVisionAsync(
        List<MoveProposal> proposals,
        CancellationToken cancellationToken)
    {
        if (!this.librarianOptions.VisionEnabled)
        {
            return;
        }

        var captioned = 0;
        for (var index = 0; index < proposals.Count && captioned < this.librarianOptions.MaxCaptionsPerPass; index++)
        {
            if (await this.TryEnrichAsync(proposals, index, cancellationToken).ConfigureAwait(false))
            {
                captioned++;
            }
        }

        if (captioned > 0)
        {
            this.logger.LogInformation("Media librarian: {Count} image(s) captioned locally", captioned);
        }
    }

    private async Task<bool> TryEnrichAsync(
        List<MoveProposal> proposals,
        int index,
        CancellationToken cancellationToken)
    {
        var proposal = proposals[index];
        if (!proposal.Reason.StartsWith("photos", StringComparison.Ordinal))
        {
            return false;
        }

        var caption = await this.TryCaptionAsync(proposal.From, cancellationToken).ConfigureAwait(false);
        if (caption is null)
        {
            return false;
        }

        proposals[index] = proposal with { Reason = $"{proposal.Reason}; vision: {caption}" };
        return true;
    }

    private async Task<string?> TryCaptionAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var image = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            var caption = await this.visionClient.DescribeAsync(
                image, "One short caption and 3 comma-separated tags.", cancellationToken)
                .ConfigureAwait(false);
            return caption.ReplaceLineEndings(" ").Trim();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            this.logger.LogWarning(exception, "Vision failed for {Path}; keeping the plain proposal", path);
            return null;
        }
    }

    private async Task<string> WriteManifestAsync(
        List<MoveProposal> proposals,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(this.librarianOptions.ManifestDirectory);

        var now = this.clock.GetUtcNow();
        var manifestPath = Path.Combine(
            this.librarianOptions.ManifestDirectory,
            $"media-librarian-{now:yyyyMMdd-HHmmss}.json");

        var manifest = new Manifest(now, "PROPOSAL ONLY - nothing has been executed", proposals);
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(manifest, manifestFormat),
            cancellationToken).ConfigureAwait(false);

        return manifestPath;
    }

    /// <summary>One proposed move. A record of intent, never of action.</summary>
    public sealed record MoveProposal(string From, string To, string Reason);

    /// <summary>The manifest a pass writes.</summary>
    public sealed record Manifest(
        DateTimeOffset GeneratedAt,
        string Status,
        IReadOnlyList<MoveProposal> Proposals);
}
