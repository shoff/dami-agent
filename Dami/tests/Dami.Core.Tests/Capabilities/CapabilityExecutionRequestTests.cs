using System.Text.Json;
using Dami.Contracts.Capabilities;
using Xunit;

namespace Dami.Core.Tests.Capabilities;

public sealed class CapabilityExecutionRequestTests
{
    [Theory]
    [InlineData(true, false, "traceId")]
    [InlineData(false, true, "spanId")]
    public void Constructor_Should_Reject_Empty_Provenance(
        bool emptyTrace,
        bool emptySpan,
        string parameterName)
    {
        var arguments = JsonSerializer.SerializeToElement(new { path = "notes.txt" });
        var invocation = new CapabilityInvocation(Guid.NewGuid(), arguments);

        var exception = Assert.Throws<ArgumentException>(() => new CapabilityExecutionRequest(
            emptyTrace ? Guid.Empty : Guid.NewGuid(),
            emptySpan ? Guid.Empty : Guid.NewGuid(),
            invocation));

        Assert.Equal(parameterName, exception.ParamName);
    }
}
