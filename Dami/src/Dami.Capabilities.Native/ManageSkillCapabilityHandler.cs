using System.Security.Cryptography;
using System.Text.Json;
using Dami.Contracts.Capabilities;

namespace Dami.Capabilities.Native;

/// <summary>Authors, revises, or retires procedural skills through the durable lifecycle.</summary>
[NativeCapability(
    "a227f20a-2440-44ea-842c-471fab6fc57b",
    "manage-skill",
    "Author, revise, or retire a procedural skill without executing its content.",
    "native://manage-skill/schema/v1",
    "1.0.0",
    ParametersJson = """
        {"type":"object","properties":{"operation":{"type":"string","enum":["author","revise","retire"]},"skillId":{"type":"string","format":"uuid"},"expectedVersion":{"type":"string"},"name":{"type":"string"},"description":{"type":"string"},"body":{"type":"string"},"tags":{"type":"array","items":{"type":"string"}},"relatedCapabilities":{"type":"array","items":{"type":"string","format":"uuid"}},"references":{"type":"object","additionalProperties":{"type":"string"}},"diff":{"type":"string"}},"required":["operation","skillId","diff"],"additionalProperties":false}
        """,
    Tags = new[] { "skills", "authoring", "procedure" })]
public sealed class ManageSkillCapabilityHandler : INativeCapabilityHandler
{
    private static readonly Guid changeIdNamespace =
        new("ef8bad3c-d677-4ea2-bb52-c9aa0c7f55b5");
    private static readonly Guid spanIdNamespace =
        new("2f4a9f42-263f-4476-9590-fea14b940f78");

    private readonly ISkillLifecycleService lifecycle;

    /// <summary>Creates the native skill lifecycle handler.</summary>
    public ManageSkillCapabilityHandler(ISkillLifecycleService lifecycle)
    {
        ArgumentNullException.ThrowIfNull(lifecycle);
        this.lifecycle = lifecycle;
    }

    /// <inheritdoc />
    public async Task<CapabilityExecutionResult> ExecuteAsync(
        CapabilityExecutionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var arguments = SkillCommandArguments.Parse(request.Invocation.Arguments);
        var change = new SkillChangeRequest(
            DeriveId(changeIdNamespace, request.TraceId, request.SpanId),
            request.TraceId,
            DeriveId(spanIdNamespace, request.TraceId, request.SpanId),
            request.SpanId,
            request.Origin,
            arguments.Kind,
            arguments.SkillId,
            arguments.ExpectedVersion,
            arguments.Replacement);
        SkillChangeRecord accepted = await this.lifecycle
            .ApplyAsync(change, arguments.Diff, cancellationToken).ConfigureAwait(false);
        return CreateResult(accepted);
    }

    private static Guid DeriveId(Guid namespaceId, Guid traceId, Guid spanId)
    {
        Span<byte> input = stackalloc byte[48];
        namespaceId.TryWriteBytes(input[..16]);
        traceId.TryWriteBytes(input[16..32]);
        spanId.TryWriteBytes(input[32..]);
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(input, hash);
        return new Guid(hash[..16]);
    }

    private static CapabilityExecutionResult CreateResult(SkillChangeRecord accepted)
    {
        var evidence = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["change_id"] = accepted.Request.ChangeId.ToString("D"),
            ["skill_id"] = accepted.Request.SkillId.ToString("D"),
            ["operation"] = OperationName(accepted.Request.Kind),
            ["materialized"] = "true",
        };
        AddIfPresent(evidence, "expected_version", accepted.Request.ExpectedVersion);
        AddIfPresent(evidence, "replacement_version", accepted.ReplacementVersion);
        return new CapabilityExecutionResult(SuccessMessage(accepted.Request.Kind), evidence);
    }

    private static void AddIfPresent(
        IDictionary<string, string> evidence,
        string key,
        string? value)
    {
        if (value is not null)
        {
            evidence.Add(key, value);
        }
    }

    private static string OperationName(SkillChangeKind kind)
    {
        return kind switch
        {
            SkillChangeKind.Author => "author",
            SkillChangeKind.Revise => "revise",
            SkillChangeKind.Retire => "retire",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static string SuccessMessage(SkillChangeKind kind)
    {
        return kind switch
        {
            SkillChangeKind.Author => "Skill authored and published.",
            SkillChangeKind.Revise => "Skill revised and published.",
            SkillChangeKind.Retire => "Skill retired and unpublished.",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private sealed record SkillCommandArguments(
        SkillChangeKind Kind,
        Guid SkillId,
        string? ExpectedVersion,
        SkillDocument? Replacement,
        string Diff)
    {
        public static SkillCommandArguments Parse(JsonElement source)
        {
            SkillChangeKind kind = ReadKind(source);
            Guid skillId = ReadGuid(source, "skillId");
            string? expectedVersion = kind == SkillChangeKind.Author
                ? null
                : ReadString(source, "expectedVersion");
            SkillDocument? document = kind == SkillChangeKind.Retire
                ? null
                : CreateDocument(source, skillId);
            return new SkillCommandArguments(
                kind, skillId, expectedVersion, document, ReadString(source, "diff"));
        }

        private static SkillDocument CreateDocument(JsonElement source, Guid skillId)
        {
            return new SkillDocument(
                skillId, ReadString(source, "name"), ReadString(source, "description"),
                ReadString(source, "body"), ReadStrings(source, "tags"),
                ReadGuids(source, "relatedCapabilities"), ReadReferences(source));
        }

        private static SkillChangeKind ReadKind(JsonElement source)
        {
            return ReadString(source, "operation") switch
            {
                "author" => SkillChangeKind.Author,
                "revise" => SkillChangeKind.Revise,
                "retire" => SkillChangeKind.Retire,
                _ => throw new ArgumentException(
                    "The skill operation must be 'author', 'revise', or 'retire'.", nameof(source)),
            };
        }

        private static string ReadString(JsonElement source, string propertyName)
        {
            if (!source.TryGetProperty(propertyName, out JsonElement property)
                || property.ValueKind != JsonValueKind.String
                || property.GetString() is not { Length: > 0 } value)
            {
                throw new ArgumentException(
                    $"Manage-skill requires a non-empty string '{propertyName}'.", nameof(source));
            }

            return value;
        }

        private static Guid ReadGuid(JsonElement source, string propertyName)
        {
            if (!source.TryGetProperty(propertyName, out JsonElement property)
                || property.ValueKind != JsonValueKind.String
                || !property.TryGetGuid(out Guid value)
                || value == Guid.Empty)
            {
                throw new ArgumentException(
                    $"Manage-skill requires a non-empty UUID '{propertyName}'.", nameof(source));
            }

            return value;
        }

        private static IReadOnlyList<string> ReadStrings(JsonElement source, string propertyName)
        {
            if (!TryReadArray(source, propertyName, out JsonElement property))
            {
                return Array.Empty<string>();
            }

            var values = new List<string>(property.GetArrayLength());
            foreach (JsonElement item in property.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String || item.GetString() is not { Length: > 0 } value)
                {
                    throw new ArgumentException(
                        $"Manage-skill requires non-empty strings in '{propertyName}'.", nameof(source));
                }

                values.Add(value);
            }

            return values;
        }

        private static IReadOnlyList<Guid> ReadGuids(JsonElement source, string propertyName)
        {
            if (!TryReadArray(source, propertyName, out JsonElement property))
            {
                return Array.Empty<Guid>();
            }

            var values = new List<Guid>(property.GetArrayLength());
            foreach (JsonElement item in property.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String
                    || !item.TryGetGuid(out Guid value) || value == Guid.Empty)
                {
                    throw new ArgumentException(
                        $"Manage-skill requires non-empty UUIDs in '{propertyName}'.", nameof(source));
                }

                values.Add(value);
            }

            return values;
        }

        private static bool TryReadArray(
            JsonElement source,
            string propertyName,
            out JsonElement property)
        {
            if (!source.TryGetProperty(propertyName, out property))
            {
                return false;
            }

            if (property.ValueKind != JsonValueKind.Array)
            {
                throw new ArgumentException(
                    $"Manage-skill requires an array '{propertyName}'.", nameof(source));
            }

            return true;
        }

        private static IReadOnlyDictionary<string, string> ReadReferences(JsonElement source)
        {
            if (!source.TryGetProperty("references", out JsonElement property))
            {
                return new Dictionary<string, string>();
            }

            if (property.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("Manage-skill requires an object 'references'.", nameof(source));
            }

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (JsonProperty item in property.EnumerateObject())
            {
                if (item.Value.ValueKind != JsonValueKind.String || item.Value.GetString() is not { } value)
                {
                    throw new ArgumentException(
                        "Manage-skill references must contain string values.", nameof(source));
                }

                values.Add(item.Name, value);
            }

            return values;
        }
    }
}
