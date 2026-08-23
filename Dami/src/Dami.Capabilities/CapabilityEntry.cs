namespace Dami.Capabilities;

/// <summary>One source-neutral capability registration.</summary>
public sealed record CapabilityEntry
{
    /// <summary>Initializes a source-neutral capability registration.</summary>
    public CapabilityEntry(
        Guid capabilityId,
        string name,
        string description,
        CapabilityKind kind,
        CapabilitySource source,
        TrustLevel trust,
        IReadOnlyList<string> tags,
        string? schemaReference,
        string? bodyReference,
        IReadOnlyList<Guid> relatedCapabilities,
        string version,
        DateTimeOffset registeredAt)
    {
        ArgumentNullException.ThrowIfNull(tags);
        ArgumentNullException.ThrowIfNull(relatedCapabilities);
        if (capabilityId == Guid.Empty)
        {
            throw new ArgumentException(
                "A capability requires a non-empty stable identifier.",
                nameof(capabilityId));
        }

        if (kind == CapabilityKind.Tool && string.IsNullOrWhiteSpace(schemaReference))
        {
            throw new ArgumentException(
                "A tool capability requires a typed schema reference.",
                nameof(schemaReference));
        }

        if (kind == CapabilityKind.Skill && string.IsNullOrWhiteSpace(bodyReference))
        {
            throw new ArgumentException(
                "A skill capability requires a body reference.",
                nameof(bodyReference));
        }

        if (kind != CapabilityKind.Tool && schemaReference is not null)
        {
            throw new ArgumentException(
                "Only a tool capability may have a schema reference.",
                nameof(schemaReference));
        }

        if (kind != CapabilityKind.Skill && bodyReference is not null)
        {
            throw new ArgumentException(
                "Only a skill capability may have a body reference.",
                nameof(bodyReference));
        }

        this.CapabilityId = capabilityId;
        this.Name = name;
        this.Description = description;
        this.Kind = kind;
        this.Source = source;
        this.Trust = trust;
        this.Tags = Array.AsReadOnly(tags.ToArray());
        this.SchemaReference = schemaReference;
        this.BodyReference = bodyReference;
        this.RelatedCapabilities = Array.AsReadOnly(relatedCapabilities.ToArray());
        this.Version = version;
        this.RegisteredAt = registeredAt;
    }

    /// <summary>Gets the stable capability identifier.</summary>
    public Guid CapabilityId { get; }

    /// <summary>Gets the capability name.</summary>
    public string Name { get; }

    /// <summary>Gets the compact retrieval description.</summary>
    public string Description { get; }

    /// <summary>Gets the capability kind.</summary>
    public CapabilityKind Kind { get; }

    /// <summary>Gets the capability source.</summary>
    public CapabilitySource Source { get; }

    /// <summary>Gets the trust assigned to source-provided content.</summary>
    public TrustLevel Trust { get; }

    /// <summary>Gets retrieval tags.</summary>
    public IReadOnlyList<string> Tags { get; }

    /// <summary>Gets the typed tool-schema reference, when this is a tool.</summary>
    public string? SchemaReference { get; }

    /// <summary>Gets the progressively disclosed body reference, when this is a skill.</summary>
    public string? BodyReference { get; }

    /// <summary>Gets capabilities referenced by this capability.</summary>
    public IReadOnlyList<Guid> RelatedCapabilities { get; }

    /// <summary>Gets the capability contract version.</summary>
    public string Version { get; }

    /// <summary>Gets when the capability was registered.</summary>
    public DateTimeOffset RegisteredAt { get; }
}
