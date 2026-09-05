using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Models;

namespace Dami.Host;

/// <summary>Creates images explicitly requested in an interactive user conversation.</summary>
public sealed class InteractiveImageGenerator
{
    private readonly IImageGenerator generator;

    /// <summary>Creates the interactive adapter.</summary>
    public InteractiveImageGenerator(IImageGenerator generator)
    {
        ArgumentNullException.ThrowIfNull(generator);
        this.generator = generator;
    }

    /// <summary>Generates one egressable image request.</summary>
    public Task<GeneratedImage> GenerateAsync(string prompt, CancellationToken cancellationToken) =>
        this.generator.GenerateAsync(
            new ImageRequest(
                prompt, "GUI image request", PrivacyClass.Egressable,
                Guid.NewGuid(), ExecutionOrigin.UserTurn),
            cancellationToken);
}
