using System.Net;
using System.Text.Json;
using Dami.Contracts.Capabilities;
using Dami.Contracts.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Dami.Providers.Tests;

public sealed class OllamaToolCallingChatClientTests
{
    [Fact]
    public async Task NextAsync_Should_Send_Only_Selected_Schemas_And_Map_The_Stable_Id()
    {
        var readSchema = CreateSchema(Guid.NewGuid(), "read_file");
        var processSchema = CreateSchema(Guid.NewGuid(), "run_process");
        var handler = new RecordingHandler("""
            {"message":{"role":"assistant","content":"","tool_calls":[{"id":"call-provider-1","function":{"name":"run_process","arguments":{"executable":"git","arguments":["status"]}}}]},"done":true}
            """);
        var client = CreateClient(handler);

        var turn = await client.NextAsync(
            "inspect the repository", [readSchema, processSchema], [], CancellationToken.None);

        Assert.Equal(processSchema.CapabilityId, turn.Invocation!.CapabilityId);
        Assert.Equal("call-provider-1", turn.CallId);
        Assert.Equal("git", turn.Invocation.Arguments.GetProperty("executable").GetString());
        using var request = JsonDocument.Parse(handler.RequestBody);
        Assert.Equal(
            ["read_file", "run_process"],
            request.RootElement.GetProperty("tools").EnumerateArray()
                .Select(item => item.GetProperty("function").GetProperty("name").GetString()));
        Assert.Equal("/api/chat", handler.RequestUri!.AbsolutePath);
        Assert.Equal("qwen3:8b", request.RootElement.GetProperty("model").GetString());
        Assert.True(request.RootElement.GetProperty("think").GetBoolean());
        Assert.False(request.RootElement.GetProperty("stream").GetBoolean());
        Assert.Equal(1200, request.RootElement.GetProperty("options").GetProperty("num_predict").GetInt32());
    }

    [Fact]
    public async Task NextAsync_Should_Reconstruct_Assistant_Call_And_Tool_Result_History()
    {
        var schema = CreateSchema(Guid.NewGuid(), "read_file");
        var arguments = JsonSerializer.SerializeToElement(new { path = "notes.txt" });
        var invocation = new CapabilityInvocation(schema.CapabilityId, arguments);
        var result = new CapabilityExecutionResult(
            "remember the gate", new Dictionary<string, string> { ["path"] = "notes.txt" });
        var exchange = new ToolExecutionExchange("ollama-0", invocation, result);
        var handler = new RecordingHandler(
            """{"message":{"role":"assistant","content":"The gate is remembered."},"done":true}""");
        var client = CreateClient(handler);

        var turn = await client.NextAsync(
            "read my notes", [schema], [exchange], CancellationToken.None);

        Assert.Equal("The gate is remembered.", turn.Answer);
        using var request = JsonDocument.Parse(handler.RequestBody);
        var messages = request.RootElement.GetProperty("messages");
        Assert.Equal(["user", "assistant", "tool"],
            messages.EnumerateArray().Select(item => item.GetProperty("role").GetString()));
        Assert.Equal("read_file", messages[1].GetProperty("tool_calls")[0]
            .GetProperty("function").GetProperty("name").GetString());
        Assert.Equal("ollama-0", messages[1].GetProperty("tool_calls")[0]
            .GetProperty("id").GetString());
        Assert.Equal("notes.txt", messages[1].GetProperty("tool_calls")[0]
            .GetProperty("function").GetProperty("arguments").GetProperty("path").GetString());
        Assert.Equal("remember the gate", messages[2].GetProperty("content").GetString());
        Assert.Equal("read_file", messages[2].GetProperty("tool_name").GetString());
    }

    [Fact]
    public async Task NextAsync_Should_Reject_Duplicate_Function_Names_Before_The_Request()
    {
        var first = CreateSchema(Guid.NewGuid(), "read_file");
        var second = CreateSchema(Guid.NewGuid(), "read_file");
        var handler = new RecordingHandler(
            """{"message":{"role":"assistant","content":"unused"},"done":true}""");
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => client.NextAsync(
            "read my notes", [first, second], [], CancellationToken.None));

        Assert.Equal("toolSchemas", exception.ParamName);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task NextAsync_Should_Reject_Non_Object_Provider_Arguments_As_Invalid_Data()
    {
        var schema = CreateSchema(Guid.NewGuid(), "read_file");
        var handler = new RecordingHandler("""
            {"message":{"role":"assistant","content":"","tool_calls":[{"function":{"name":"read_file","arguments":[]}}]},"done":true}
            """);
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => client.NextAsync(
            "read my notes", [schema], [], CancellationToken.None));

        Assert.Contains("arguments", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NextAsync_Should_Reject_Duplicate_Stable_Ids_Before_The_Request()
    {
        var capabilityId = Guid.NewGuid();
        var first = CreateSchema(capabilityId, "read_file");
        var second = CreateSchema(capabilityId, "read_text");
        var handler = new RecordingHandler(
            """{"message":{"role":"assistant","content":"unused"},"done":true}""");
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => client.NextAsync(
            "read my notes", [first, second], [], CancellationToken.None));

        Assert.Equal("toolSchemas", exception.ParamName);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task NextAsync_Should_Reject_An_Unadvertised_Provider_Tool_Call()
    {
        var schema = CreateSchema(Guid.NewGuid(), "read_file");
        var handler = new RecordingHandler("""
            {"message":{"role":"assistant","content":"","tool_calls":[{"function":{"name":"run_process","arguments":{}}}]},"done":true}
            """);
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => client.NextAsync(
            "read my notes", [schema], [], CancellationToken.None));

        Assert.Contains("unadvertised", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NextAsync_Should_Reject_Multiple_Calls_In_One_Provider_Step()
    {
        var schema = CreateSchema(Guid.NewGuid(), "read_file");
        var handler = new RecordingHandler("""
            {"message":{"role":"assistant","content":"","tool_calls":[{"function":{"name":"read_file","arguments":{}}},{"function":{"name":"read_file","arguments":{}}}]},"done":true}
            """);
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => client.NextAsync(
            "read my notes", [schema], [], CancellationToken.None));

        Assert.Contains("more than one", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static CapabilityToolSchema CreateSchema(Guid capabilityId, string name)
    {
        var parameters = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new { },
        });
        return new CapabilityToolSchema(capabilityId, name, $"Use {name}.", parameters);
    }

    private static OllamaToolCallingChatClient CreateClient(RecordingHandler handler)
    {
        return new OllamaToolCallingChatClient(
            new HttpClient(handler),
            Options.Create(new OllamaOptions()),
            NullLogger<OllamaToolCallingChatClient>.Instance);
    }

    private sealed class RecordingHandler(string responseBody) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        public string RequestBody { get; private set; } = string.Empty;

        public Uri? RequestUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            this.CallCount++;
            this.RequestUri = request.RequestUri;
            this.RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody),
            };
        }
    }
}
