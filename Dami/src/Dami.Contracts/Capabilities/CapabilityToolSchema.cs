using System.Text.Json;

namespace Dami.Contracts.Capabilities;

/// <summary>A source-neutral tool schema advertised to a model for one selected capability.</summary>
public sealed class CapabilityToolSchema
{
    private const int MAX_NAME_LENGTH = 64;

    /// <summary>Creates an immutable advertised tool schema.</summary>
    public CapabilityToolSchema(
        Guid capabilityId,
        string name,
        string description,
        JsonElement parameters)
    {
        if (capabilityId == Guid.Empty)
        {
            throw new ArgumentException(
                "An advertised tool requires a non-empty stable identifier.", nameof(capabilityId));
        }

        ValidateName(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        if (parameters.ValueKind != JsonValueKind.Object
            || !parameters.TryGetProperty("type", out var schemaType)
            || schemaType.ValueKind != JsonValueKind.String
            || !schemaType.ValueEquals("object"u8))
        {
            throw new ArgumentException(
                "Tool parameters must describe a JSON object schema.", nameof(parameters));
        }

        this.CapabilityId = capabilityId;
        this.Name = name;
        this.Description = description;
        this.Parameters = parameters.Clone();
    }

    /// <summary>Gets the stable runtime capability identifier.</summary>
    public Guid CapabilityId { get; }

    /// <summary>Gets the portable function name advertised to the model.</summary>
    public string Name { get; }

    /// <summary>Gets the short tool description advertised to the model.</summary>
    public string Description { get; }

    /// <summary>Gets the owned JSON Schema object for the function arguments.</summary>
    public JsonElement Parameters { get; }

    private static void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > MAX_NAME_LENGTH)
        {
            throw new ArgumentOutOfRangeException(
                nameof(name), name.Length, $"Tool names cannot exceed {MAX_NAME_LENGTH} characters.");
        }

        foreach (var character in name)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-')
            {
                throw new ArgumentException(
                    "Tool names may contain only ASCII letters, digits, underscores, and hyphens.",
                    nameof(name));
            }
        }
    }
}
