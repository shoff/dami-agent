using System.Text.Json;

namespace Dami.Contracts.Capabilities;

/// <summary>One source-neutral request to execute a capability by stable identifier.</summary>
public sealed class CapabilityInvocation
{
    /// <summary>Creates an invocation and snapshots its JSON arguments.</summary>
    public CapabilityInvocation(Guid capabilityId, JsonElement arguments)
    {
        if (capabilityId == Guid.Empty)
        {
            throw new ArgumentException(
                "A capability invocation requires a non-empty stable identifier.",
                nameof(capabilityId));
        }

        if (arguments.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Capability arguments must be a JSON object.", nameof(arguments));
        }

        this.CapabilityId = capabilityId;
        this.Arguments = arguments.Clone();
    }

    /// <summary>Gets the stable capability identifier.</summary>
    public Guid CapabilityId { get; }

    /// <summary>Gets the snapshotted JSON argument object.</summary>
    public JsonElement Arguments { get; }
}
