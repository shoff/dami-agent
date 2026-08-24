using Dami.Contracts.Capabilities;
using Dami.Contracts.Events;

namespace Dami.Persistence.Skills;

internal static class SkillChangeEventFactory
{
    public static ExecutionEvent Requested(SkillChangeRecord record)
    {
        SkillChangeRequest request = record.Request;
        var metadata = new Dictionary<string, string>
        {
            ["kind"] = request.Kind.ToString(),
            ["skill_id"] = request.SkillId.ToString("D"),
        };
        AddOptional(metadata, "expected_version", request.ExpectedVersion);
        AddOptional(metadata, "replacement_version", record.ReplacementVersion);
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
            $"skill-change://{request.ChangeId:D}",
            metadata);
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
}
