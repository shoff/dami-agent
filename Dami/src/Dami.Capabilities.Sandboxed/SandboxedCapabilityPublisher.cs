using Dami.Contracts.Capabilities;

namespace Dami.Capabilities.Sandboxed;

/// <summary>Publishes sandbox handler, schema, then retrievable metadata with rollback.</summary>
public sealed class SandboxedCapabilityPublisher
{
    private readonly IRevertibleRegistrar<SandboxedCapabilityRegistration> handlers;
    private readonly IRevertibleRegistrar<CapabilityToolSchema> schemas;
    private readonly IRevertibleRegistrar<CapabilityEntry> capabilities;

    /// <summary>Creates the failure-atomic publication coordinator.</summary>
    public SandboxedCapabilityPublisher(
        IRevertibleRegistrar<SandboxedCapabilityRegistration> handlers,
        IRevertibleRegistrar<CapabilityToolSchema> schemas,
        IRevertibleRegistrar<CapabilityEntry> capabilities)
    {
        ArgumentNullException.ThrowIfNull(handlers);
        ArgumentNullException.ThrowIfNull(schemas);
        ArgumentNullException.ThrowIfNull(capabilities);
        this.handlers = handlers;
        this.schemas = schemas;
        this.capabilities = capabilities;
    }

    /// <summary>Publishes dependencies before metadata or removes this exact activation.</summary>
    public void Publish(
        SandboxedCapabilityRegistration registration,
        CapabilityToolSchema schema,
        CapabilityEntry entry)
    {
        Validate(registration, schema, entry);
        this.handlers.Register(registration);
        try
        {
            this.schemas.Register(schema);
            try
            {
                this.capabilities.Register(entry);
            }
            catch
            {
                this.schemas.TryRemoveExact(schema);
                throw;
            }
        }
        catch
        {
            this.handlers.TryRemoveExact(registration);
            throw;
        }
    }

    private static void Validate(
        SandboxedCapabilityRegistration registration,
        CapabilityToolSchema schema,
        CapabilityEntry entry)
    {
        ArgumentNullException.ThrowIfNull(registration);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(entry);
        if (registration.CapabilityId != schema.CapabilityId
            || schema.CapabilityId != entry.CapabilityId
            || entry.Source != CapabilitySource.Sandboxed
            || entry.Kind != CapabilityKind.Tool
            || !string.Equals(
                registration.ArtifactVersion, entry.Version, StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Sandboxed handler, schema, and metadata must describe one exact tool activation.");
        }
    }
}
