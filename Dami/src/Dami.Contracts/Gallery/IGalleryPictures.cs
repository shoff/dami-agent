using Dami.Contracts.Models;

namespace Dami.Contracts.Gallery;

/// <summary>The bytes behind a Gallery file name, for anything that wants to show one.</summary>
public interface IGalleryPictures
{
    /// <summary>Loads a picture the Gallery holds, or null when it does not.</summary>
    Task<GeneratedImage?> LoadAsync(string fileName, CancellationToken cancellationToken);
}
