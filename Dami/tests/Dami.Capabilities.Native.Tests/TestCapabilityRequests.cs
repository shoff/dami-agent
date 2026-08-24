using System.Text.Json;
using Dami.Contracts.Capabilities;
using Dami.Contracts.Context;
using Dami.Contracts.Events;

namespace Dami.Capabilities.Native.Tests;

internal static class TestCapabilityRequests
{
    internal static CapabilityExecutionRequest Create(JsonElement arguments)
    {
        return Create(Guid.NewGuid(), arguments);
    }

    internal static CapabilityExecutionRequest Create(Guid capabilityId, JsonElement arguments)
    {
        return new CapabilityExecutionRequest(
            Guid.NewGuid(), Guid.NewGuid(), PrivacyClass.LocalOnly, ExecutionOrigin.UserTurn,
            new CapabilityInvocation(capabilityId, arguments));
    }
}
