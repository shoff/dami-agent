using Dami.Core.Sessions;
using Xunit;

namespace Dami.Core.Tests.Sessions;

public sealed class SessionCancellationRegistryTests
{
    [Fact]
    public async Task InterruptAsync_Should_Cancel_The_Current_Generation_And_Resume_A_Fresh_One()
    {
        var registry = new SessionCancellationRegistry();
        var sessionId = Guid.NewGuid();
        var interrupted = registry.TokenFor(sessionId);

        await registry.InterruptAsync(sessionId, CancellationToken.None);
        registry.Resume(sessionId);
        var resumed = registry.TokenFor(sessionId);

        Assert.True(interrupted.IsCancellationRequested);
        Assert.False(resumed.IsCancellationRequested);
        Assert.NotEqual(interrupted, resumed);
    }
}
