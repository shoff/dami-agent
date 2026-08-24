using System.Text.Json;

namespace Dami.Providers;

/// <summary>Shared wire-format settings for native Ollama HTTP adapters.</summary>
internal static class OllamaJson
{
    internal static JsonSerializerOptions SerializerOptions { get; } =
        new(JsonSerializerDefaults.Web);
}
