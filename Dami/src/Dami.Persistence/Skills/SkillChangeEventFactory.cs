using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Dami.Contracts.Capabilities;
using Dami.Contracts.Events;

namespace Dami.Persistence.Skills;

internal static class SkillChangeEventFactory
{
    private static readonly Guid successNamespace =
        new("3edc53c7-e203-4272-bab9-a8c99108bc91");
    private static readonly Guid failureNamespace =
        new("97d078c1-e26d-47ef-91f5-dd72ef24272d");

    public static ExecutionEvent Requested(SkillChangeRecord record)
    {
        SkillChangeRequest request = record.Request;
        return new ExecutionEvent(
            request.ChangeId,
            request.TraceId,
            request.SpanId,
            request.ParentSpanId,
            request.Origin,
            "skills:lifecycle",
            ExecutionEventType.SkillChangeRequested,
            ExecutionStatus.Running,
            record.RequestedAt,
            RequestedLabel(request.Kind),
            PayloadReference(request.ChangeId),
            CreateMetadata(record));
    }

    public static ExecutionEvent Succeeded(
        SkillChangeRecord record,
        DateTimeOffset occurredAt)
    {
        SkillChangeRequest request = record.Request;
        return new ExecutionEvent(
            DeriveId(successNamespace, request.ChangeId),
            request.TraceId,
            request.SpanId,
            request.ParentSpanId,
            request.Origin,
            "skills:lifecycle",
            ExecutionEventType.SkillChanged,
            ExecutionStatus.Succeeded,
            occurredAt,
            "skill change materialized",
            PayloadReference(request.ChangeId),
            CreateMetadata(record));
    }

    public static ExecutionEvent Failed(
        SkillChangeRecord record,
        string failureCode,
        DateTimeOffset occurredAt)
    {
        ValidateFailureCode(failureCode);
        SkillChangeRequest request = record.Request;
        Dictionary<string, string> metadata = CreateMetadata(record);
        metadata.Add("failure_code", failureCode);
        return new ExecutionEvent(
            DeriveFailureId(request.ChangeId, failureCode, occurredAt),
            request.TraceId, request.SpanId,
            request.ParentSpanId, request.Origin, "skills:lifecycle",
            ExecutionEventType.SkillChangeFailed, ExecutionStatus.Failed, occurredAt,
            "skill change materialization failed", PayloadReference(request.ChangeId), metadata);
    }

    private static string RequestedLabel(SkillChangeKind kind)
    {
        return kind switch
        {
            SkillChangeKind.Author => "skill author accepted",
            SkillChangeKind.Revise => "skill revise accepted",
            SkillChangeKind.Retire => "skill retire accepted",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private static void AddOptional(
        IDictionary<string, string> metadata,
        string key,
        string? value)
    {
        if (value is not null)
        {
            metadata.Add(key, value);
        }
    }

    private static Dictionary<string, string> CreateMetadata(SkillChangeRecord record)
    {
        SkillChangeRequest request = record.Request;
        var metadata = new Dictionary<string, string>
        {
            ["kind"] = request.Kind.ToString(),
            ["skill_id"] = request.SkillId.ToString("D"),
        };
        AddOptional(metadata, "expected_version", request.ExpectedVersion);
        AddOptional(metadata, "replacement_version", record.ReplacementVersion);
        return metadata;
    }

    private static Guid DeriveId(Guid namespaceId, Guid changeId)
    {
        Span<byte> input = stackalloc byte[32];
        namespaceId.TryWriteBytes(input[..16]);
        changeId.TryWriteBytes(input[16..]);
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(input, hash);
        return new Guid(hash[..16]);
    }

    private static Guid DeriveFailureId(
        Guid changeId,
        string failureCode,
        DateTimeOffset occurredAt)
    {
        int codeBytes = Encoding.UTF8.GetByteCount(failureCode);
        Span<byte> input = stackalloc byte[40 + codeBytes];
        failureNamespace.TryWriteBytes(input[..16]);
        changeId.TryWriteBytes(input[16..32]);
        long microseconds = occurredAt.UtcDateTime.Ticks / TimeSpan.TicksPerMicrosecond;
        BinaryPrimitives.WriteInt64LittleEndian(input[32..40], microseconds);
        Encoding.UTF8.GetBytes(failureCode, input[40..]);
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(input, hash);
        return new Guid(hash[..16]);
    }

    private static void ValidateFailureCode(string failureCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureCode);
        if (failureCode.Length > 120 || failureCode.AsSpan().IndexOfAny('\r', '\n') >= 0)
        {
            throw new ArgumentException(
                "A failure code must be one line of at most 120 characters.", nameof(failureCode));
        }
    }

    private static string PayloadReference(Guid changeId)
    {
        return $"skill-change://{changeId:D}";
    }
}
