using Dami.Contracts.Models;

namespace Dami.Host.Discord;

/// <summary>Makes a new picture of Dami herself, identity preserved, for the gateway.</summary>
/// <remarks>
/// The Gallery generator that does this lives in the Host, above this assembly, so the
/// gateway asks through a seam and the Host supplies it. Keeps the identity anchor and
/// the Gallery's persistence in one place rather than copied into every channel.
/// </remarks>
public interface IDiscordPortraitGenerator
{
    /// <summary>Generates one portrait of Dami in the given scene.</summary>
    Task<GeneratedImage> GenerateAsync(string scene, CancellationToken cancellationToken);
}
