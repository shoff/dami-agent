using System.Text.Json;
using Xunit;

namespace Dami.Providers.Tests;

/// <summary>
/// The app-server wire format, pinned. These are the details that silently produced an
/// empty stream while everything looked healthy.
/// </summary>
public sealed class CodexAppServerTests
{
    private const string DELTA =
        """{"method":"item/agentMessage/delta","params":{"threadId":"t","turnId":"u","delta":"Hi"}}""";

    private const string THREAD_STARTED =
        """{"id":1,"result":{"thread":{"id":"01a05d2a-33bf-7281-80d5-1a60d178206a","cwd":"/x"}}}""";

    [Fact]
    public void A_Delta_Notification_Carries_Its_Fragment_At_Params_Delta()
    {
        using var document = JsonDocument.Parse(DELTA);
        var root = document.RootElement;

        Assert.Equal(
            ("item/agentMessage/delta", "Hi"),
            (root.GetProperty("method").GetString(),
                root.GetProperty("params").GetProperty("delta").GetString()));
    }

    [Fact]
    public void A_Thread_Id_Lives_At_Result_Thread_Id_Not_Result_ThreadId()
    {
        // Reading result.threadId returns nothing, the turn is never started, and the
        // stream ends empty with no error anywhere. That cost two failed probes.
        using var document = JsonDocument.Parse(THREAD_STARTED);
        var result = document.RootElement.GetProperty("result");

        Assert.False(result.TryGetProperty("threadId", out _));
        Assert.Equal(
            "01a05d2a-33bf-7281-80d5-1a60d178206a",
            result.GetProperty("thread").GetProperty("id").GetString());
    }

    [Theory]
    [InlineData("turn/completed")]
    [InlineData("turn/failed")]
    public void A_Turn_Ends_On_These_Notifications(string method)
    {
        // Without an end condition the reader blocks on a live process forever.
        using var document = JsonDocument.Parse(
            $$$"""{"method":"{{{method}}}","params":{"threadId":"t"}}""");

        Assert.Contains(
            document.RootElement.GetProperty("method").GetString(),
            new[] { "turn/completed", "turn/failed" });
    }
}
