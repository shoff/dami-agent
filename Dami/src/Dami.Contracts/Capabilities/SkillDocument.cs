using System.Collections.ObjectModel;

namespace Dami.Contracts.Capabilities;

/// <summary>The complete text content of one authored skill revision.</summary>
public sealed record SkillDocument
{
    /// <summary>Creates an immutable skill revision document.</summary>
    public SkillDocument(
        Guid skillId,
        string name,
        string description,
        string body,
        IReadOnlyList<string> tags,
        IReadOnlyList<Guid> relatedCapabilities,
        IReadOnlyDictionary<string, string> references)
    {
        if (skillId == Guid.Empty)
        {
            throw new ArgumentException("A skill document requires a stable identifier.", nameof(skillId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(relatedCapabilities);
        ArgumentNullException.ThrowIfNull(references);
        this.SkillId = skillId;
        this.Name = name;
        this.Description = description;
        this.Body = body;
        this.Tags = Array.AsReadOnly(tags.ToArray());
        this.RelatedCapabilities = Array.AsReadOnly(relatedCapabilities.ToArray());
        this.References = SnapshotReferences(references);
    }

    /// <summary>Gets the stable skill identifier.</summary>
    public Guid SkillId { get; }

    /// <summary>Gets the retrieval name.</summary>
    public string Name { get; }

    /// <summary>Gets the retrieval description.</summary>
    public string Description { get; }

    /// <summary>Gets the procedural Markdown body.</summary>
    public string Body { get; }

    /// <summary>Gets the retrieval tags.</summary>
    public IReadOnlyList<string> Tags { get; }

    /// <summary>Gets stable capabilities required by this procedure.</summary>
    public IReadOnlyList<Guid> RelatedCapabilities { get; }

    /// <summary>Gets explicitly bundled relative text files by path.</summary>
    public IReadOnlyDictionary<string, string> References { get; }

    private static IReadOnlyDictionary<string, string> SnapshotReferences(
        IReadOnlyDictionary<string, string> references)
    {
        var snapshot = new Dictionary<string, string>(references.Count, StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> pair in references)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pair.Key);
            ArgumentNullException.ThrowIfNull(pair.Value);
            snapshot.Add(pair.Key, pair.Value);
        }

        return new ReadOnlyDictionary<string, string>(snapshot);
    }
}
