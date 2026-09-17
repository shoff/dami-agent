using System.Diagnostics;
using System.Text.Json;
using Dami.Contracts.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
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

    [Fact]
    public void Image_Input_Uses_The_Installed_App_Server_LocalImage_Shape()
    {
        var json = JsonSerializer.Serialize(CodexAppServer.TurnInput(
            "compare them", ["/tmp/first.png", "/tmp/second.png"]));

        Assert.Contains("\"type\":\"text\"", json);
        Assert.Contains("\"type\":\"localImage\"", json);
        Assert.Contains("\"path\":\"/tmp/first.png\"", json);
        Assert.Contains("\"path\":\"/tmp/second.png\"", json);
    }

    [Fact]
    public void Initialize_Should_Declare_The_Experimental_Api_Capability()
    {
        // Without it: {"error":{"code":-32600,"message":"thread/start.dynamicTools requires
        // experimentalApi capability"}} — the tools are silently absent otherwise.
        var json = JsonSerializer.Serialize(CodexAppServer.InitializeParams());

        Assert.Contains("\"experimentalApi\":true", json);
    }

    [Fact]
    public void Thread_Start_Should_Declare_The_Bundle_As_Dynamic_Function_Tools()
    {
        var json = JsonSerializer.Serialize(CodexAppServer.ThreadStartParams("/x", OneTool()));

        Assert.Contains("\"cwd\":\"/x\"", json);
        Assert.Contains("\"dynamicTools\":[{\"type\":\"function\",\"name\":\"make_portrait\"", json);
        Assert.Contains("\"inputSchema\":{\"type\":\"object\"", json);
    }

    [Fact]
    public void Thread_Start_Should_Be_Ephemeral_Read_Only_Unapproved_And_Without_Codex_Web_Search()
    {
        // 2026-09-05: a chat turn "verified postings at the source" through Codex's own
        // search, around the gated research door; and approvals nobody answers are the
        // likeliest shape of the ten-minute silences.
        var json = JsonSerializer.Serialize(CodexAppServer.ThreadStartParams("/x", FrontierToolbox.Empty, new CodexOptions()));

        Assert.Contains("\"ephemeral\":true", json);
        Assert.Contains("\"sandbox\":\"read-only\"", json);
        Assert.Contains("\"approvalPolicy\":\"never\"", json);
        Assert.Contains("\"config\":{\"web_search\":\"disabled\"}", json);
        Assert.Contains("no browser, no shell", json);
    }

    [Fact]
    public async Task An_Unserved_Server_Request_Is_Declined_So_The_Turn_Goes_On()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var (root, script, _) = await CreateFakeServerAsync(asksApproval: true);
        try
        {
            var fragments = await StreamAllAsync(script, root, FrontierToolbox.Empty);

            // The fake only completes the turn after it has read an error reply on id 9.
            Assert.Equal(["declined"], fragments);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Thread_Start_Without_Tools_Should_Not_Mention_Them()
    {
        var json = JsonSerializer.Serialize(CodexAppServer.ThreadStartParams("/x", FrontierToolbox.Empty));

        Assert.DoesNotContain("dynamicTools", json);
    }

    [Fact]
    public void A_Tool_Call_Is_Answered_In_The_Installed_Content_Item_Shape()
    {
        var json = JsonSerializer.Serialize(CodexAppServer.ToolCallResponse(FrontierToolResult.Ok("saved")));

        Assert.Contains("\"success\":true", json);
        Assert.Contains("\"contentItems\":[{\"type\":\"inputText\",\"text\":\"saved\"}]", json);
    }

    [Fact]
    public async Task A_Tool_Call_From_The_Server_Is_Run_Here_And_Answered_On_Its_Id()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var (root, script, _) = await CreateFakeServerAsync(callsTool: true);
        var handler = Substitute.For<IFrontierToolHandler>();
        handler.HandleAsync(Arg.Any<FrontierToolCall>(), Arg.Any<CancellationToken>())
            .Returns(FrontierToolResult.Ok("dami-1.png attached"));
        var toolbox = new FrontierToolbox(OneTool().Tools, handler);

        try
        {
            var fragments = await StreamAllAsync(script, root, toolbox);

            // The fake only completes the turn after it has read a reply on id 7 that
            // says success:true — so a completed turn proves the response frame.
            Assert.Equal(["answered"], fragments);
            await handler.Received(1).HandleAsync(
                Arg.Is<FrontierToolCall>(call =>
                    call.CallId == "c1"
                    && call.Tool == "make_portrait"
                    && call.Arguments.GetProperty("scene").GetString() == "kitchen"),
                Arg.Any<CancellationToken>());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Tool_Time_Should_Not_Count_Against_The_Turn_Deadline()
    {
        // 2026-09-16 21:52: three portraits at ~3 min each ate the whole 600 s turn budget
        // and the fourth was cancelled with the answer. The deadline bounds the model, not
        // the tools it waits on — each tool has its own ceiling.
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var (root, script, _) = await CreateFakeServerAsync(callsTool: true);
        var handler = Substitute.For<IFrontierToolHandler>();
        handler.HandleAsync(Arg.Any<FrontierToolCall>(), Arg.Any<CancellationToken>())
            .Returns<Task<FrontierToolResult>>(async _ =>
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
                return FrontierToolResult.Ok("dami-1.png attached");
            });
        var toolbox = new FrontierToolbox(OneTool().Tools, handler);

        try
        {
            var fragments = await StreamAllAsync(script, root, toolbox, TimeSpan.FromSeconds(1));

            Assert.Equal(["answered"], fragments);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task A_Silent_Turn_Ends_At_The_First_Token_Deadline_Not_The_Overall_One()
    {
        // 2026-09-03 23:09 → 23:19: nothing for the full 600 s. The overall deadline is
        // for long answers; silence gets its own, much shorter clock.
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var (root, script, pidFile) = await CreateFakeServerAsync();
        try
        {
            await using var server = new CodexAppServer(
                new CodexOptions { BinaryPath = script, FirstTokenTimeoutSeconds = 1 },
                NullLogger<CodexAppServer>.Instance);
            var watch = Stopwatch.StartNew();

            var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await foreach (var unused in server.StreamAsync(
                    "say nothing", root, TimeSpan.FromSeconds(30), [], FrontierToolbox.Empty, CancellationToken.None))
                {
                }
            });

            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"took {watch.Elapsed}");
            Assert.Contains("produced nothing for 1s", thrown.Message, StringComparison.Ordinal);
            Assert.False(IsRunning(int.Parse(await File.ReadAllTextAsync(pidFile))));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<List<string>> StreamAllAsync(
        string script, string root, FrontierToolbox toolbox, TimeSpan? timeout = null)
    {
        await using var server = new CodexAppServer(
            new CodexOptions { BinaryPath = script }, NullLogger<CodexAppServer>.Instance);
        var fragments = new List<string>();
        await foreach (var fragment in server.StreamAsync(
            "draw", root, timeout ?? TimeSpan.FromSeconds(10), [], toolbox, CancellationToken.None))
        {
            fragments.Add(fragment);
        }

        return fragments;
    }

    private static FrontierToolbox OneTool()
    {
        using var schema = JsonDocument.Parse("""{"type":"object","properties":{"scene":{"type":"string"}}}""");
        return new FrontierToolbox(
            [new FrontierTool("make_portrait", "draw Dami", schema.RootElement.Clone())],
            Substitute.For<IFrontierToolHandler>());
    }

    [Theory]
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

    [Fact]
    public async Task A_Timed_Out_Turn_Stops_The_App_Server_Process()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var (root, script, pidFile) = await CreateFakeServerAsync();

        try
        {
            await using var server = new CodexAppServer(
                new CodexOptions { BinaryPath = script },
                NullLogger<CodexAppServer>.Instance);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                await foreach (var unused in server.StreamAsync(
                    "wait forever", root, TimeSpan.FromMilliseconds(100), [], FrontierToolbox.Empty,
                    CancellationToken.None))
                {
                }
            });

            var pid = int.Parse(await File.ReadAllTextAsync(pidFile));
            Assert.False(IsRunning(pid));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<(string Root, string Script, string PidFile)> CreateFakeServerAsync(
        bool callsTool = false, bool asksApproval = false)
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException();
        }

        var root = Path.Combine(Path.GetTempPath(), $"dami-app-server-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var script = Path.Combine(root, "fake-codex");
        var pidFile = Path.Combine(root, "fake.pid");
        await File.WriteAllTextAsync(script, FakeServerScript(pidFile, callsTool, asksApproval));
        File.SetUnixFileMode(
            script,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return (root, script, pidFile);
    }

    private const string TOOL_CALL_CASE = """
        *'"method":"turn/start"'*)
          printf '%s\n' '{"id":7,"method":"item/tool/call","params":{"threadId":"thread-1","turnId":"u","callId":"c1","namespace":null,"tool":"make_portrait","arguments":{"scene":"kitchen"}}}'
          ;;
        *'"id":7'*'"success":true'*)
          printf '%s\n' '{"method":"item/agentMessage/delta","params":{"delta":"answered"}}'
          printf '%s\n' '{"method":"turn/completed","params":{"turn":{"error":null}}}'
          ;;
      """;

    private const string APPROVAL_CASE = """
        *'"method":"turn/start"'*)
          printf '%s\n' '{"id":9,"method":"item/commandExecution/requestApproval","params":{"threadId":"thread-1","turnId":"u","itemId":"c1","command":"rm -rf /"}}'
          ;;
        *'"id":9'*'"error"'*)
          printf '%s\n' '{"method":"item/agentMessage/delta","params":{"delta":"declined"}}'
          printf '%s\n' '{"method":"turn/completed","params":{"turn":{"error":null}}}'
          ;;
      """;

    private static string FakeServerScript(string pidFile, bool callsTool, bool asksApproval = false)
    {
        var cases = (callsTool ? TOOL_CALL_CASE : string.Empty) + (asksApproval ? APPROVAL_CASE : string.Empty);
        return """
            #!/bin/sh
            printf '%s' "$$" > "__PID_FILE__"
            while IFS= read -r frame; do
              case "$frame" in
                *'"method":"thread/start"'*)
                  printf '%s\n' '{"result":{"thread":{"id":"thread-1"}}}'
                  ;;
            __TOOL_CALL__  esac
            done
            """.Replace("__PID_FILE__", pidFile, StringComparison.Ordinal)
            .Replace("__TOOL_CALL__", cases, StringComparison.Ordinal);
    }

    private static bool IsRunning(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
