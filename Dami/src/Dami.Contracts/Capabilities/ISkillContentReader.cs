namespace Dami.Contracts.Capabilities;

/// <summary>Reads content belonging to an already-published skill version.</summary>
public interface ISkillContentReader
{
    /// <summary>Reads the selected skill's procedural body.</summary>
    Task<string> ReadBodyAsync(
        Guid skillId,
        string expectedVersion,
        CancellationToken cancellationToken);

    /// <summary>Reads one explicitly declared bundled text file on demand.</summary>
    Task<string> ReadReferenceAsync(
        Guid skillId,
        string expectedVersion,
        string relativePath,
        CancellationToken cancellationToken);
}
