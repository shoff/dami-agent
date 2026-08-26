using Dami.Contracts.TaskBoard;
using Dami.Contracts.Context;
using Dami.Contracts.Events;
using Npgsql;

namespace Dami.Persistence.TaskBoard;

internal sealed record BoardRow(
    Guid BoardId,
    string Title,
    string FeatureRequest,
    string Plan,
    TaskOrdering RootOrdering,
    TaskBoardPlanningContext? PlanningContext,
    TaskActor CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    internal static BoardRow Read(NpgsqlDataReader reader)
    {
        return new BoardRow(
            reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
            Enum.Parse<TaskOrdering>(reader.GetString(4)),
            ReadPlanningContext(reader),
            new TaskActor(reader.GetString(8), Enum.Parse<TaskActorKind>(reader.GetString(9))),
            reader.GetFieldValue<DateTimeOffset>(10), reader.GetFieldValue<DateTimeOffset>(11));
    }

    internal TaskBoardSnapshot WithTasks(IReadOnlyList<BoardTask> tasks)
    {
        return new TaskBoardSnapshot(
            this.BoardId, this.Title, this.FeatureRequest, this.Plan, this.CreatedBy,
            this.CreatedAt, this.UpdatedAt, TaskBoardStatusDeriver.Derive(tasks),
            this.RootOrdering, tasks,
            this.PlanningContext);
    }

    private static TaskBoardPlanningContext? ReadPlanningContext(NpgsqlDataReader reader)
    {
        return reader.IsDBNull(5)
            ? null
            : new TaskBoardPlanningContext(
                Enum.Parse<FeaturePlannerKind>(reader.GetString(5)),
                Enum.Parse<PrivacyClass>(reader.GetString(6)),
                Enum.Parse<ExecutionOrigin>(reader.GetString(7)));
    }
}

internal sealed class TaskRow
{
    private TaskRow(
        Guid taskId,
        Guid? parentTaskId,
        string title,
        string description,
        TaskBoardStatus status,
        TaskPriority priority,
        int position,
        TaskOrdering subTaskOrdering,
        TaskClaim? claim,
        long version,
        DateTimeOffset createdAt)
    {
        this.TaskId = taskId;
        this.ParentTaskId = parentTaskId;
        this.Title = title;
        this.Description = description;
        this.Status = status;
        this.Priority = priority;
        this.Position = position;
        this.SubTaskOrdering = subTaskOrdering;
        this.Claim = claim;
        this.Version = version;
        this.CreatedAt = createdAt;
    }

    internal Guid TaskId { get; }

    internal Guid? ParentTaskId { get; }

    internal string Title { get; }

    internal string Description { get; }

    internal TaskBoardStatus Status { get; }

    internal TaskPriority Priority { get; }

    internal int Position { get; }

    internal TaskOrdering SubTaskOrdering { get; }

    internal TaskClaim? Claim { get; }

    internal long Version { get; }

    internal DateTimeOffset CreatedAt { get; }

    internal List<Guid> Prerequisites { get; } = [];

    internal List<AcceptanceCriterion> Criteria { get; } = [];

    internal static TaskRow Read(NpgsqlDataReader reader)
    {
        TaskClaim? claim = reader.IsDBNull(8)
            ? null
            : new TaskClaim(
                new TaskActor(reader.GetString(8), Enum.Parse<TaskActorKind>(reader.GetString(9))),
                reader.GetFieldValue<DateTimeOffset>(10));
        return new TaskRow(
            reader.GetGuid(0), reader.IsDBNull(1) ? null : reader.GetGuid(1),
            reader.GetString(2), reader.GetString(3),
            Enum.Parse<TaskBoardStatus>(reader.GetString(4)),
            (TaskPriority)reader.GetInt16(5), reader.GetInt32(6),
            Enum.Parse<TaskOrdering>(reader.GetString(7)), claim, reader.GetInt64(11),
            reader.GetFieldValue<DateTimeOffset>(12));
    }

    internal BoardTask ToTask(IReadOnlyList<BoardTask> children)
    {
        return new BoardTask(
            this.TaskId, this.Title, this.Description, this.Status, this.Priority,
            this.Position, this.SubTaskOrdering, this.Claim, this.Version,
            this.Prerequisites.ToArray(), this.Criteria.ToArray(), children, this.CreatedAt);
    }
}
