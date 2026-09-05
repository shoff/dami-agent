using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Dami.Host.Tests.Observability;

public sealed class DamiRequestLogScopeTests
{
    [Fact]
    public void Create_Should_IncludeTheRequestIdentifier()
    {
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "request-123";

        var scope = DamiRequestLogScope.Create(context);

        Assert.Contains(scope, value => value.Key == "RequestId" && (string)value.Value! == "request-123");
    }

    [Fact]
    public void Create_Should_IncludeTheActiveTraceIdentifier()
    {
        using var activity = new Activity("test").Start();

        var scope = DamiRequestLogScope.Create(new DefaultHttpContext());

        Assert.Contains(scope, value => value.Key == "TraceId" && (string)value.Value! == activity.TraceId.ToHexString());
    }

    [Fact]
    public void Create_Should_ExcludeRequestBodies()
    {
        var scope = DamiRequestLogScope.Create(new DefaultHttpContext());

        Assert.DoesNotContain(scope, value => value.Key.Contains("body", StringComparison.OrdinalIgnoreCase));
    }
}
