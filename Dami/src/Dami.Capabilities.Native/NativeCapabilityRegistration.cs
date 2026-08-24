using Dami.Contracts.Capabilities;

namespace Dami.Capabilities.Native;

/// <summary>Associates normalized registry metadata with its native implementation type.</summary>
public sealed record NativeCapabilityRegistration(
    Type ImplementationType,
    CapabilityEntry Entry,
    CapabilityToolSchema Schema);
