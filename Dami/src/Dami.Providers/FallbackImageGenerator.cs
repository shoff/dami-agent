using Dami.Contracts.Context;
using Dami.Contracts.Models;
using Microsoft.Extensions.Logging;

namespace Dami.Providers;

/// <summary>Draws on the primary door and, when no picture comes back, retries on the backup.</summary>
/// <remarks>
/// Steve, 2026-09-16: Codex stays primary; if the image does not generate there, retry it
/// on Gemini. "Does not generate" is any refusal or failure of the primary — not enabled,
/// budget spent, no file, timeout — with one exception: a prompt that is not Egressable
/// was refused for what it contains, and a second door is not an answer to that (D-012).
/// Each door records its own events, so the ledger shows the primary's failure and then
/// the backup's request, in that order.
/// </remarks>
public sealed class FallbackImageGenerator : IImageGenerator
{
    private readonly IImageGenerator primary;
    private readonly IImageGenerator backup;
    private readonly ILogger<FallbackImageGenerator> logger;

    /// <summary>Creates the pair.</summary>
    public FallbackImageGenerator(
        IImageGenerator primary, IImageGenerator backup, ILogger<FallbackImageGenerator> logger)
    {
        ArgumentNullException.ThrowIfNull(primary);
        ArgumentNullException.ThrowIfNull(backup);
        ArgumentNullException.ThrowIfNull(logger);
        this.primary = primary;
        this.backup = backup;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<GeneratedImage> GenerateAsync(ImageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        try
        {
            return await this.primary.GenerateAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
            when (exception is not OperationCanceledException && request.Privacy == PrivacyClass.Egressable)
        {
            this.logger.LogWarning(
                exception, "Primary image provider produced no picture for {Purpose}; retrying on the backup",
                request.Purpose);
        }

        return await this.backup.GenerateAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
