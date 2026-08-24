namespace Dami.Capabilities.Skills;

internal sealed class SkillDirectoryLocator
{
    private readonly SkillCapabilityLoader inspector;
    private readonly int maxSkills;
    private readonly string rootDirectory;

    public SkillDirectoryLocator(SkillLoaderOptions options)
    {
        this.rootDirectory = Path.GetFullPath(options.RootDirectory);
        this.maxSkills = options.MaxSkills;
        this.inspector = new SkillCapabilityLoader(new CapabilityRegistry(), options);
    }

    public async Task<SkillDirectoryIdentity?> FindAsync(
        Guid skillId,
        CancellationToken cancellationToken)
    {
        SkillDirectoryIdentity? found = null;
        foreach (string directory in Directory.EnumerateDirectories(this.rootDirectory))
        {
            if (IsInternal(directory))
            {
                continue;
            }

            SkillDirectoryIdentity candidate = await this.inspector
                .InspectAsync(directory, cancellationToken).ConfigureAwait(false);
            if (candidate.SkillId == skillId)
            {
                if (found is not null)
                {
                    throw new InvalidDataException($"Skill '{skillId}' occupies multiple directories.");
                }

                found = candidate;
            }
        }

        return found;
    }

    public void EnsureCapacityForNew()
    {
        var count = 0;
        foreach (string directory in Directory.EnumerateDirectories(this.rootDirectory))
        {
            if (!IsInternal(directory) && ++count >= this.maxSkills)
            {
                throw new InvalidDataException(
                    $"Skill root already holds its configured limit of {this.maxSkills} folders.");
            }
        }
    }

    public Task<SkillDirectoryIdentity> InspectAsync(
        string directory,
        CancellationToken cancellationToken)
    {
        return this.inspector.InspectAsync(directory, cancellationToken);
    }

    private static bool IsInternal(string directory)
    {
        return Path.GetFileName(directory.AsSpan()).StartsWith(
            ".dami-", StringComparison.Ordinal);
    }
}
