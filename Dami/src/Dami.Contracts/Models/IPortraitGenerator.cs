namespace Dami.Contracts.Models;

/// <summary>Makes a new picture of Dami herself, identity preserved.</summary>
/// <remarks>
/// The generator that does this owns the identity anchor and the Gallery, above the
/// runtime; channels and tool bundles ask through this seam so the anchor and the
/// persistence stay in one place rather than copied into every caller.
/// </remarks>
public interface IPortraitGenerator
{
    /// <summary>Generates one portrait of Dami in the given scene.</summary>
    Task<GeneratedImage> GenerateAsync(string scene, CancellationToken cancellationToken);
}
