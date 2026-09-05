using Microsoft.AspNetCore.Http;
using Xunit;

namespace Dami.Host.Tests.Observability;

public sealed class DamiRequestLoggingPolicyTests
{
    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("OPTIONS")]
    public void ShouldLogInformation_Should_ReturnFalseForReadOnlyRequests(string method)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;

        var shouldLog = DamiRequestLoggingPolicy.ShouldLogInformation(context);

        Assert.False(shouldLog);
    }

    [Fact]
    public void ShouldLogInformation_Should_ReturnTrueForAnInteractiveTurn()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;

        var shouldLog = DamiRequestLoggingPolicy.ShouldLogInformation(context);

        Assert.True(shouldLog);
    }
}
