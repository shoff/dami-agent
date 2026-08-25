namespace Dami.Core.TaskBoard;

internal static class FeaturePlanPrompt
{
    internal static string Create(string featureRequest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(featureRequest);
        return $$"""
            Create an implementation plan for this feature request:

            {{featureRequest}}

            Return only JSON. Do not wrap it in Markdown. Use this exact shape:
            {
              "title": "short feature title",
              "plan": "concise implementation plan",
              "rootOrdering": "Ordered or Priority",
              "tasks": [{
                "key": "stable-unique-key",
                "title": "task title",
                "description": "bounded scope",
                "priority": "Low, Normal, High, or Critical",
                "position": 0,
                "subTaskOrdering": "Ordered or Priority",
                "prerequisiteKeys": ["keys that must finish first"],
                "acceptanceCriteria": ["objective observable result"],
                "subTasks": []
              }]
            }
            Every prerequisite key must name a task in this response. Subtasks have the
            same structure as root tasks. Split work finely enough for one actor to claim.
            """;
    }
}
