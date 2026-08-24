namespace Dami.Contracts.Capabilities;

/// <summary>A selected skill whose body remains behind progressive disclosure.</summary>
public sealed record SkillSelection
{
    /// <summary>Creates an immutable selected-skill reference.</summary>
    public SkillSelection(
        Guid capabilityId,
        string name,
        string bodyReference,
        string version)
    {
        if (capabilityId == Guid.Empty)
        {
            throw new ArgumentException(
                "A selected skill requires a non-empty stable identifier.", nameof(capabilityId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(bodyReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        this.CapabilityId = capabilityId;
        this.Name = name;
        this.BodyReference = bodyReference;
        this.Version = version;
    }

    /// <summary>Gets the stable skill identifier.</summary>
    public Guid CapabilityId { get; }

    /// <summary>Gets the display name used in the prompt.</summary>
    public string Name { get; }

    /// <summary>Gets the opaque progressively disclosed body reference.</summary>
    public string BodyReference { get; }

    /// <summary>Gets the selected content version.</summary>
    public string Version { get; }
}
