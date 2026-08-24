namespace Dami.Capabilities.Native;

/// <summary>Declares the registry metadata for an in-process native tool.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class NativeCapabilityAttribute : Attribute
{
    /// <summary>Initializes native tool metadata.</summary>
    public NativeCapabilityAttribute(
        string capabilityId,
        string name,
        string description,
        string schemaReference,
        string version)
    {
        this.CapabilityId = capabilityId;
        this.Name = name;
        this.Description = description;
        this.SchemaReference = schemaReference;
        this.Version = version;
    }

    /// <summary>Gets the stable capability identifier.</summary>
    public string CapabilityId { get; }

    /// <summary>Gets the capability name.</summary>
    public string Name { get; }

    /// <summary>Gets the compact retrieval description.</summary>
    public string Description { get; }

    /// <summary>Gets the typed schema reference.</summary>
    public string SchemaReference { get; }

    /// <summary>Gets or sets the JSON object schema advertised to the model.</summary>
    public string ParametersJson { get; set; } = string.Empty;

    /// <summary>Gets the capability contract version.</summary>
    public string Version { get; }

    /// <summary>Gets or sets retrieval tags.</summary>
    public string[] Tags { get; set; } = [];
}
