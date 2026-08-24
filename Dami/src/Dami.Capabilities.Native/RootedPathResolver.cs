namespace Dami.Capabilities.Native;

/// <summary>Resolves existing paths while enforcing one canonical directory root.</summary>
internal sealed class RootedPathResolver
{
    private readonly StringComparison pathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
    private readonly string rootDirectory;

    public RootedPathResolver(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        var root = new DirectoryInfo(Path.GetFullPath(rootDirectory));
        if (!root.Exists)
        {
            throw new DirectoryNotFoundException(root.FullName);
        }

        this.rootDirectory = root.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? root.FullName;
    }

    public string ResolveFile(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathRooted(relativePath))
        {
            throw new UnauthorizedAccessException("Paths must be relative to the configured root.");
        }

        var lexicalPath = Path.GetFullPath(relativePath, this.rootDirectory);
        this.EnsureContained(lexicalPath);
        var normalized = Path.GetRelativePath(this.rootDirectory, lexicalPath);
        var segments = normalized.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        return this.ResolveSegments(segments);
    }

    private string ResolveSegments(IReadOnlyList<string> segments)
    {
        var current = this.rootDirectory;
        for (var index = 0; index < segments.Count; index++)
        {
            var candidate = Path.Combine(current, segments[index]);
            var entry = CreateEntry(candidate, isFile: index == segments.Count - 1);
            current = entry.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? entry.FullName;
            this.EnsureContained(current);
        }

        return current;
    }

    private static FileSystemInfo CreateEntry(string path, bool isFile)
    {
        return isFile ? new FileInfo(path) : new DirectoryInfo(path);
    }

    private void EnsureContained(string path)
    {
        var relative = Path.GetRelativePath(this.rootDirectory, path);
        if (Path.IsPathRooted(relative)
            || relative == ".."
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", this.pathComparison))
        {
            throw new UnauthorizedAccessException("Path escapes the configured root.");
        }
    }
}
