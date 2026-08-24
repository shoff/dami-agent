using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Dami.Contracts.Privacy;
using Xunit;

namespace Dami.Privacy.Tests;

public sealed class AmbientEgressOperationContextTests
{
    [Fact]
    public void Dispose_Should_Allow_Recovery_After_An_OutOfOrder_Attempt()
    {
        var contexts = new AmbientEgressOperationContext();
        IDisposable outer = contexts.Begin(CreateContext());
        IDisposable inner = contexts.Begin(CreateContext());

        Assert.Throws<InvalidOperationException>(outer.Dispose);
        inner.Dispose();
        outer.Dispose();

        Assert.Null(contexts.Current);
    }

    [Fact]
    public async Task Begin_Should_Isolate_Concurrent_Async_Flows()
    {
        var contexts = new AmbientEgressOperationContext();
        EgressOperationContext first = CreateContext();
        EgressOperationContext second = CreateContext();

        Guid[] observed = await Task.WhenAll(
            ObserveAsync(contexts, first), ObserveAsync(contexts, second));

        Assert.Equal([first.TraceId, second.TraceId], observed);
    }

    [Fact]
    public void Constructor_Should_Reject_A_Multiline_Event_Purpose()
    {
        Assert.Throws<ArgumentException>("purpose", () => new EgressOperationContext(
            "safe label\nsecret body", PrivacyClass.Egressable,
            Guid.NewGuid(), Guid.NewGuid(), ExecutionOrigin.UserTurn));
    }

    private static EgressOperationContext CreateContext()
    {
        return new EgressOperationContext(
            "test", PrivacyClass.Egressable,
            Guid.NewGuid(), Guid.NewGuid(), ExecutionOrigin.UserTurn);
    }

    private static async Task<Guid> ObserveAsync(
        AmbientEgressOperationContext contexts,
        EgressOperationContext context)
    {
        using IDisposable scope = contexts.Begin(context);
        await Task.Yield();
        return contexts.Current!.TraceId;
    }
}
