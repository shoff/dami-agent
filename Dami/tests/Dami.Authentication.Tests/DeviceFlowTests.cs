using Xunit;

namespace Dami.Authentication.Tests;

public sealed class DeviceFlowTests
{
    [Fact]
    public void ReadAuthorization_Should_Read_A_Device_Response()
    {
        var authorization = DeviceFlow.ReadAuthorization(
            """
            {"device_code":"dev-1","user_code":"ABCD-EFGH",
             "verification_uri":"http://127.0.0.1:5810/connect/verify",
             "verification_uri_complete":"http://127.0.0.1:5810/connect/verify?code=ABCD-EFGH",
             "expires_in":600,"interval":5}
            """);

        Assert.NotNull(authorization);
        Assert.Equal("dev-1", authorization.DeviceCode);
        Assert.Equal("ABCD-EFGH", authorization.UserCode);
        Assert.Equal(TimeSpan.FromSeconds(5), authorization.Interval);
        Assert.Equal(TimeSpan.FromMinutes(10), authorization.ExpiresIn);
        Assert.NotNull(authorization.VerificationUriComplete);
    }

    [Fact]
    public void ReadAuthorization_Should_Default_The_Interval_When_The_Server_Omits_It()
    {
        // RFC 8628 makes interval optional and says five seconds when absent. Defaulting to
        // zero would poll in a tight loop and earn an immediate slow_down.
        var authorization = DeviceFlow.ReadAuthorization(
            """{"device_code":"d","user_code":"u","verification_uri":"http://127.0.0.1:5810/v"}""");

        Assert.NotNull(authorization);
        Assert.Equal(TimeSpan.FromSeconds(5), authorization.Interval);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("""{"device_code":"d"}""")]
    [InlineData("""{"user_code":"u","verification_uri":"http://x/v"}""")]
    [InlineData("""{"device_code":"d","user_code":"u","verification_uri":"not a uri"}""")]
    public void ReadAuthorization_Should_Refuse_An_Incomplete_Response(string json)
    {
        Assert.Null(DeviceFlow.ReadAuthorization(json));
    }

    [Fact]
    public void ReadPoll_Should_Read_A_Granted_Token()
    {
        var poll = DeviceFlow.ReadPoll(
            """{"access_token":"at-1","refresh_token":"rt-1","expires_in":3600}""");

        Assert.Equal(DevicePollResult.Granted, poll.Result);
        Assert.NotNull(poll.Token);
        Assert.Equal("at-1", poll.Token.AccessToken);
        Assert.Equal("rt-1", poll.Token.RefreshToken);
        Assert.Equal(TimeSpan.FromHours(1), poll.Token.ExpiresIn);
    }

    [Theory]
    [InlineData("authorization_pending", DevicePollResult.Pending)]
    [InlineData("slow_down", DevicePollResult.SlowDown)]
    [InlineData("access_denied", DevicePollResult.Denied)]
    [InlineData("expired_token", DevicePollResult.Expired)]
    [InlineData("invalid_client", DevicePollResult.Failed)]
    public void ReadPoll_Should_Classify_Each_Error(string error, DevicePollResult expected)
    {
        Assert.Equal(expected, DeviceFlow.ReadPoll($$"""{"error":"{{error}}"}""").Result);
    }

    [Fact]
    public void Slow_Down_Should_Not_Be_Treated_As_A_Failure()
    {
        // The case device-flow clients get wrong. slow_down is an instruction to back off,
        // not an error: aborting on it kills a login that was about to succeed.
        Assert.NotEqual(DevicePollResult.Failed, DeviceFlow.ReadPoll("""{"error":"slow_down"}""").Result);
    }

    [Fact]
    public void NextInterval_Should_Increase_By_Five_Seconds_On_Slow_Down()
    {
        Assert.Equal(
            TimeSpan.FromSeconds(10),
            DeviceFlow.NextInterval(TimeSpan.FromSeconds(5), DevicePollResult.SlowDown));
    }

    [Fact]
    public void NextInterval_Should_Keep_The_Increase_Rather_Than_Reverting()
    {
        // RFC 8628 §3.5: the increase persists. Dropping back to the original interval
        // earns another slow_down at once and turns the login into a fight with the server.
        var once = DeviceFlow.NextInterval(TimeSpan.FromSeconds(5), DevicePollResult.SlowDown);
        var then = DeviceFlow.NextInterval(once, DevicePollResult.Pending);

        Assert.Equal(TimeSpan.FromSeconds(10), then);
    }

    [Fact]
    public void ReadPoll_Should_Fail_Loudly_On_A_Response_With_Neither_Token_Nor_Error()
    {
        var poll = DeviceFlow.ReadPoll("{}");

        Assert.Equal(DevicePollResult.Failed, poll.Result);
        Assert.NotNull(poll.Error);
    }
}
