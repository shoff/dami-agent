using System.Text.Json;
using System.Text.Json.Serialization;
using Dami.Contracts.TaskBoard;

namespace Dami.Core.TaskBoard;

internal static class FeaturePlanJsonParser
{
    private const int MAX_RESPONSE_CHARS = 262_144;

    private static readonly JsonSerializerOptions options = new()
    {
        MaxDepth = 16,
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() },
    };

    internal static FeaturePlanProposal Parse(string response)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(response);
        if (response.Length > MAX_RESPONSE_CHARS)
        {
            throw new InvalidOperationException(
                $"Planner response exceeded {MAX_RESPONSE_CHARS} characters.");
        }

        try
        {
            return JsonSerializer.Deserialize<FeaturePlanProposal>(response, options)
                ?? throw new InvalidOperationException("Planner response was JSON null.");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "Planner response was not valid task-board JSON.", exception);
        }
    }
}
