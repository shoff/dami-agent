using System.Net.Http.Json;
using System.Text.Json;
using Dami.Contracts.Capabilities;
using Dami.Contracts.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Providers;

/// <summary>Adapts Ollama's native chat tool protocol to source-neutral capability calls.</summary>
public sealed class OllamaToolCallingChatClient : IToolCallingChatClient
{
    private readonly HttpClient httpClient;
    private readonly Uri baseUri;
    private readonly string model;
    private readonly bool think;
    private readonly int maxTokens;
    private readonly ILogger<OllamaToolCallingChatClient> logger;

    /// <summary>Creates the Ollama tool-calling adapter.</summary>
    public OllamaToolCallingChatClient(
        HttpClient httpClient,
        IOptions<OllamaOptions> options,
        ILogger<OllamaToolCallingChatClient> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Value.Model);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.Value.MaxTokens, 0);
        this.httpClient = httpClient;
        this.baseUri = LocalSidecarEndpoint.Parse(options.Value.BaseUrl, nameof(options));
        this.model = options.Value.Model;
        this.think = options.Value.Think;
        this.maxTokens = options.Value.MaxTokens;
        this.logger = logger;
    }

    /// <inheritdoc />
    public async Task<ToolModelTurn> NextAsync(
        string prompt,
        IReadOnlyList<CapabilityToolSchema> toolSchemas,
        IReadOnlyList<ToolExecutionExchange> exchanges,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        ArgumentNullException.ThrowIfNull(toolSchemas);
        ArgumentNullException.ThrowIfNull(exchanges);
        ValidateSelection(toolSchemas);
        var request = this.CreateRequest(prompt, toolSchemas, exchanges);
        using var response = await this.httpClient.PostAsJsonAsync(
            new Uri(this.baseUri, "/api/chat"), request, OllamaJson.SerializerOptions, cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var body = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        return this.ParseTurn(body.RootElement, toolSchemas, exchanges.Count);
    }

    private object CreateRequest(
        string prompt,
        IReadOnlyList<CapabilityToolSchema> toolSchemas,
        IReadOnlyList<ToolExecutionExchange> exchanges)
    {
        return new
        {
            model = this.model,
            messages = CreateMessages(prompt, toolSchemas, exchanges),
            tools = CreateTools(toolSchemas),
            think = this.think,
            stream = false,
            options = new { num_predict = this.maxTokens },
        };
    }

    private static object[] CreateTools(IReadOnlyList<CapabilityToolSchema> toolSchemas)
    {
        var tools = new object[toolSchemas.Count];
        for (var index = 0; index < toolSchemas.Count; index++)
        {
            var schema = toolSchemas[index];
            tools[index] = new
            {
                type = "function",
                function = new
                {
                    name = schema.Name,
                    description = schema.Description,
                    parameters = schema.Parameters,
                },
            };
        }

        return tools;
    }

    private static void ValidateSelection(IReadOnlyList<CapabilityToolSchema> toolSchemas)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var capabilityIds = new HashSet<Guid>();
        for (var index = 0; index < toolSchemas.Count; index++)
        {
            var schema = toolSchemas[index];
            if (!names.Add(schema.Name))
            {
                throw new ArgumentException(
                    $"Selected tool name '{schema.Name}' is duplicated.",
                    nameof(toolSchemas));
            }

            if (!capabilityIds.Add(schema.CapabilityId))
            {
                throw new ArgumentException(
                    $"Selected capability id '{schema.CapabilityId}' is duplicated.",
                    nameof(toolSchemas));
            }
        }
    }

    private static object[] CreateMessages(
        string prompt,
        IReadOnlyList<CapabilityToolSchema> toolSchemas,
        IReadOnlyList<ToolExecutionExchange> exchanges)
    {
        var messages = new object[1 + (exchanges.Count * 2)];
        messages[0] = new { role = "user", content = prompt };
        for (var index = 0; index < exchanges.Count; index++)
        {
            var exchange = exchanges[index];
            var schema = FindSchema(toolSchemas, exchange.Invocation.CapabilityId);
            messages[1 + (index * 2)] = new
            {
                role = "assistant",
                content = string.Empty,
                tool_calls = new[]
                {
                    new
                    {
                        id = exchange.CallId,
                        function = new { name = schema.Name, arguments = exchange.Invocation.Arguments },
                    },
                },
            };
            messages[2 + (index * 2)] = new
            {
                role = "tool",
                tool_name = schema.Name,
                content = exchange.Result.Output,
            };
        }

        return messages;
    }

    private ToolModelTurn ParseTurn(
        JsonElement root,
        IReadOnlyList<CapabilityToolSchema> toolSchemas,
        int callOrdinal)
    {
        var message = root.GetProperty("message");
        if (!message.TryGetProperty("tool_calls", out var calls) || calls.GetArrayLength() == 0)
        {
            return ToolModelTurn.ForAnswer(message.GetProperty("content").GetString() ?? string.Empty);
        }

        if (calls.GetArrayLength() != 1)
        {
            throw new InvalidDataException("Ollama returned more than one tool call in one model step.");
        }

        var call = calls[0];
        var function = call.GetProperty("function");
        var name = function.GetProperty("name").GetString()
            ?? throw new InvalidDataException("Ollama returned a tool call without a function name.");
        var schema = FindSchema(toolSchemas, name);
        if (!function.TryGetProperty("arguments", out var arguments)
            || arguments.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Ollama tool-call arguments must be a JSON object.");
        }

        this.logger.LogDebug("Ollama requested selected tool {ToolName}", name);
        return ToolModelTurn.ForCall(
            ReadCallId(call, callOrdinal), new CapabilityInvocation(schema.CapabilityId, arguments));
    }

    private static string ReadCallId(JsonElement call, int callOrdinal)
    {
        if (call.TryGetProperty("id", out var property)
            && property.ValueKind == JsonValueKind.String
            && property.GetString() is { } callId
            && !string.IsNullOrWhiteSpace(callId))
        {
            return callId;
        }

        return $"ollama-{callOrdinal}";
    }

    private static CapabilityToolSchema FindSchema(
        IReadOnlyList<CapabilityToolSchema> toolSchemas,
        string name)
    {
        for (var index = 0; index < toolSchemas.Count; index++)
        {
            if (string.Equals(toolSchemas[index].Name, name, StringComparison.Ordinal))
            {
                return toolSchemas[index];
            }
        }

        throw new InvalidDataException($"Ollama requested unadvertised tool '{name}'.");
    }

    private static CapabilityToolSchema FindSchema(
        IReadOnlyList<CapabilityToolSchema> toolSchemas,
        Guid capabilityId)
    {
        for (var index = 0; index < toolSchemas.Count; index++)
        {
            if (toolSchemas[index].CapabilityId == capabilityId)
            {
                return toolSchemas[index];
            }
        }

        throw new InvalidDataException(
            $"Tool history references unadvertised capability '{capabilityId}'.");
    }
}
