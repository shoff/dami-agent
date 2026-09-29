using Microsoft.Extensions.Options;

namespace Dami.Proactive.Vault;

/// <summary>Writes Markdown pages into the vault folder.</summary>
public interface IVault
{
    /// <summary>Writes <paramref name="content"/> at <paramref name="relativePath"/>, replacing what was there.</summary>
    Task WriteAsync(string relativePath, string content, CancellationToken cancellationToken);
}

/// <summary>The vault on disk. Each page is written whole and moved into place, so Obsidian never reads half a file.</summary>
public sealed class FileVault : IVault
{
    private readonly string root;

    /// <summary>Creates the vault.</summary>
    public FileVault(IOptions<VaultOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.root = Path.GetFullPath(options.Value.Directory);
    }

    /// <inheritdoc />
    public async Task WriteAsync(string relativePath, string content, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(content);
        var target = Path.GetFullPath(Path.Combine(this.root, relativePath));
        if (!target.StartsWith(this.root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException($"'{relativePath}' is outside the vault", nameof(relativePath));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var staging = target + ".writing";
        await File.WriteAllTextAsync(staging, content, cancellationToken).ConfigureAwait(false);
        File.Move(staging, target, overwrite: true);
    }
}
