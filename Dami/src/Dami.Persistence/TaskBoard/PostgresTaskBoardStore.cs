using System.Data;
using System.Runtime.CompilerServices;
using Dami.Contracts.TaskBoard;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace Dami.Persistence.TaskBoard;

/// <summary>PostgreSQL persistence for collaborative feature plans and task trees.</summary>
public sealed class PostgresTaskBoardStore : ITaskBoardStore
{
    private readonly NpgsqlDataSource dataSource;
    private readonly string schema;
    private readonly string boardsTable;
    private readonly string tasksTable;
    private readonly string criteriaTable;
    private readonly string prerequisitesTable;
    private readonly string activityTable;

    /// <summary>Creates a task-board store.</summary>
    public PostgresTaskBoardStore(NpgsqlDataSource dataSource, IOptions<PostgresOptions> options)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);
        this.dataSource = dataSource;
        var schema = options.Value.SchemaName;
        this.schema = schema;
        this.boardsTable = $"{schema}.task_boards";
        this.tasksTable = $"{schema}.task_board_tasks";
        this.criteriaTable = $"{schema}.task_acceptance_criteria";
        this.prerequisitesTable = $"{schema}.task_prerequisites";
        this.activityTable = $"{schema}.task_board_activity";
    }

    /// <inheritdoc />
    public async Task CreateAsync(TaskBoardDraft draft, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var nodes = TaskDraftGraph.Flatten(draft.Tasks);
        TaskDraftGraph.Validate(nodes);
        await using var connection = await this.dataSource
            .OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var inserted = await this.InsertBoardAsync(
            connection, transaction, draft, cancellationToken).ConfigureAwait(false);
        if (!inserted)
        {
            var existing = await this.ReadSnapshotAsync(
                connection, transaction, draft.BoardId, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            if (existing is null || !TaskBoardDraftIdentity.Matches(draft, existing))
            {
                throw new InvalidOperationException(
                    $"Task board '{draft.BoardId}' already exists with different content.");
            }

            return;
        }

        await this.InsertTasksAsync(connection, transaction, draft, nodes, cancellationToken)
            .ConfigureAwait(false);
        await this.InsertActivityAsync(
            connection, transaction, draft.BoardId, null, null,
            TaskBoardActivityKind.BoardCreated, draft.CreatedBy, draft.CreatedAt,
            cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> TryAddTaskAsync(
        Guid boardId,
        Guid? parentTaskId,
        BoardTaskDraft draft,
        TaskActor actor,
        DateTimeOffset addedAt,
        string? detail,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(actor);
        var nodes = TaskDraftGraph.Flatten(draft, parentTaskId);
        await using var connection = await this.dataSource
            .OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var parentStatus = await this.ParentStatusAsync(
            connection, transaction, boardId, parentTaskId, draft.TaskId, cancellationToken).ConfigureAwait(false);
        if (parentStatus is null or "Cancelled")
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        if (parentStatus == "Done")
        {
            await this.ReopenForChildAsync(
                connection, transaction, parentTaskId!.Value, draft, actor, addedAt, cancellationToken)
                .ConfigureAwait(false);
        }

        await this.InsertSubtreeAsync(connection, transaction, boardId, addedAt, nodes, cancellationToken)
            .ConfigureAwait(false);
        await this.InsertActivityAsync(
            connection, transaction, boardId, draft.TaskId, null, TaskBoardActivityKind.TaskAdded,
            actor, addedAt, cancellationToken, detail).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task InsertSubtreeAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid boardId,
        DateTimeOffset createdAt,
        IReadOnlyList<TaskDraftNode> nodes,
        CancellationToken cancellationToken)
    {
        foreach (var node in nodes)
        {
            await this.InsertTaskAsync(connection, transaction, boardId, createdAt, node, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var node in nodes)
        {
            await this.InsertTaskRelationsAsync(connection, transaction, boardId, node.Draft, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The parent's status when the add is possible — "Open" standing in for a root add —
    /// or null when the board or parent is unknown or the task id is already taken.
    /// </summary>
    private async Task<string?> ParentStatusAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid boardId,
        Guid? parentTaskId,
        Guid taskId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"""
            select case
                     when not exists (select 1 from {this.boardsTable} where board_id = @board) then null
                     when exists (select 1 from {this.tasksTable} where task_id = @task) then null
                     when @parent is null then 'Open'
                     else (select status from {this.tasksTable}
                            where board_id = @board and task_id = @parent for update)
                   end;
            """, connection, transaction);
        command.Parameters.AddWithValue("board", boardId);
        command.Parameters.AddWithValue("parent", NpgsqlDbType.Uuid, (object?)parentTaskId ?? DBNull.Value);
        command.Parameters.AddWithValue("task", taskId);
        var status = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return status is DBNull or null ? null : (string)status;
    }

    /// <summary>A finished parent that gains a child is open again, on the record.</summary>
    private async Task ReopenForChildAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid parentTaskId,
        BoardTaskDraft child,
        TaskActor actor,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"select {this.schema}.task_board_reopen_for_child(@event, @task, @actor, @kind, @detail, @changed);",
            connection, transaction);
        command.Parameters.AddWithValue("event", Guid.NewGuid());
        command.Parameters.AddWithValue("task", parentTaskId);
        command.Parameters.AddWithValue("actor", actor.ActorId);
        command.Parameters.AddWithValue("kind", actor.Kind.ToString());
        command.Parameters.AddWithValue("detail", $"Reopened: gained the task '{child.Title}'.");
        command.Parameters.AddWithValue("changed", at);
        if (!await ExecuteBooleanAsync(command, cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"Task '{parentTaskId}' could not be reopened for its new child.");
        }
    }

    /// <inheritdoc />
    public async Task<TaskBoardSnapshot?> FindAsync(
        Guid boardId,
        CancellationToken cancellationToken)
    {
        await using var connection = await this.dataSource
            .OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(
            IsolationLevel.RepeatableRead, cancellationToken).ConfigureAwait(false);
        var snapshot = await this.ReadSnapshotAsync(
            connection, transaction, boardId, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return snapshot;
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TaskBoardSummary> ListRecentAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        var command = this.dataSource.CreateCommand(this.ListRecentSql);
        command.Parameters.AddWithValue("limit", limit);
        return StreamSummariesAsync(command, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> TryClaimAsync(
        Guid taskId,
        long expectedVersion,
        TaskActor actor,
        DateTimeOffset claimedAt,
        string? detail,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedVersion);
        ArgumentNullException.ThrowIfNull(actor);
        await using var command = this.dataSource.CreateCommand(this.ClaimSql);
        command.Parameters.AddWithValue("detail", NpgsqlDbType.Text, (object?)detail ?? DBNull.Value);
        command.Parameters.AddWithValue("event", Guid.NewGuid());
        command.Parameters.AddWithValue("task", taskId);
        command.Parameters.AddWithValue("version", expectedVersion);
        command.Parameters.AddWithValue("actor", actor.ActorId);
        command.Parameters.AddWithValue("kind", actor.Kind.ToString());
        command.Parameters.AddWithValue("claimed", claimedAt);
        return await ExecuteBooleanAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> TrySetCriterionAsync(
        Guid criterionId,
        long expectedTaskVersion,
        bool isSatisfied,
        TaskActor actor,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedTaskVersion);
        ArgumentNullException.ThrowIfNull(actor);
        await using var command = this.dataSource.CreateCommand(this.CriterionSql);
        command.Parameters.AddWithValue("event", Guid.NewGuid());
        command.Parameters.AddWithValue("criterion", criterionId);
        command.Parameters.AddWithValue("version", expectedTaskVersion);
        command.Parameters.AddWithValue("satisfied", isSatisfied);
        command.Parameters.AddWithValue("actor", actor.ActorId);
        command.Parameters.AddWithValue("kind", actor.Kind.ToString());
        command.Parameters.AddWithValue("changed", changedAt);
        return await ExecuteBooleanAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> TryCompleteAsync(
        Guid taskId,
        long expectedVersion,
        TaskActor actor,
        DateTimeOffset completedAt,
        string? detail,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedVersion);
        ArgumentNullException.ThrowIfNull(actor);
        await using var command = this.CreateCompletionCommand(
            taskId, expectedVersion, actor, completedAt);
        command.Parameters.AddWithValue("detail", NpgsqlDbType.Text, (object?)detail ?? DBNull.Value);
        return await ExecuteBooleanAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> TrySetStatusAsync(
        Guid taskId,
        long expectedVersion,
        TaskBoardStatus status,
        TaskActor actor,
        string detail,
        DateTimeOffset changedAt,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(expectedVersion);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown task status.");
        }

        await using var command = this.dataSource.CreateCommand(this.StatusSql);
        command.Parameters.AddWithValue("event", Guid.NewGuid());
        command.Parameters.AddWithValue("task", taskId);
        command.Parameters.AddWithValue("version", expectedVersion);
        command.Parameters.AddWithValue("next", status.ToString());
        command.Parameters.AddWithValue("actor", actor.ActorId);
        command.Parameters.AddWithValue("kind", actor.Kind.ToString());
        command.Parameters.AddWithValue("detail", detail);
        command.Parameters.AddWithValue("changed", changedAt);
        return await ExecuteBooleanAsync(command, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TaskBoardActivity> ActivityAsync(
        Guid boardId,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        var command = this.dataSource.CreateCommand(
            $"""
            select sequence, event_id, board_id, task_id, criterion_id, kind,
                   actor_id, actor_kind, occurred_at, from_status, to_status, detail
              from {this.activityTable}
             where board_id = @board
             order by sequence
             limit @limit;
            """);
        command.Parameters.AddWithValue("board", boardId);
        command.Parameters.AddWithValue("limit", limit);
        return StreamActivityAsync(command, cancellationToken);
    }

    private NpgsqlCommand CreateCompletionCommand(
        Guid taskId,
        long expectedVersion,
        TaskActor actor,
        DateTimeOffset completedAt)
    {
        var command = this.dataSource.CreateCommand(this.CompletionSql);
        command.Parameters.AddWithValue("event", Guid.NewGuid());
        command.Parameters.AddWithValue("task", taskId);
        command.Parameters.AddWithValue("version", expectedVersion);
        command.Parameters.AddWithValue("actor", actor.ActorId);
        command.Parameters.AddWithValue("kind", actor.Kind.ToString());
        command.Parameters.AddWithValue("completed", completedAt);
        return command;
    }

    private string ClaimSql => $"select {this.schema}.task_board_try_claim("
        + "@event, @task, @version, @actor, @kind, @claimed, @detail);";

    private string CriterionSql => $"select {this.schema}.task_board_try_set_criterion("
        + "@event, @criterion, @version, @satisfied, @actor, @kind, @changed);";

    private string CompletionSql => $"select {this.schema}.task_board_try_complete("
        + "@event, @task, @version, @actor, @kind, @completed, @detail);";

    private string StatusSql => $"select {this.schema}.task_board_try_set_status("
        + "@event, @task, @version, @next, @actor, @kind, @detail, @changed);";

    private string ListRecentSql => $"""
        select board.board_id, board.title,
               case
                   when count(task.task_id) = 0 then 'Open'
                   when bool_and(task.status = 'Cancelled') then 'Cancelled'
                   when bool_and(task.status in ('Done', 'Cancelled')) then 'Done'
                   when count(*) filter (where task.status = 'InProgress') > 0 then 'InProgress'
                   when count(*) filter (where task.status = 'Blocked') > 0
                        and count(*) filter (where task.status = 'Open') = 0 then 'Blocked'
                   else 'Open'
               end as derived_status,
               greatest(board.updated_at, coalesce(max(task.updated_at), board.updated_at)),
               count(task.task_id)::integer,
               count(*) filter (where task.status = 'Done')::integer,
               count(*) filter (where task.status = 'Blocked')::integer
          from {this.boardsTable} board
          left join {this.tasksTable} task on task.board_id = board.board_id
         group by board.board_id
         order by greatest(board.updated_at, coalesce(max(task.updated_at), board.updated_at)) desc,
                  board.board_id
         limit @limit;
        """;

    private async Task InsertActivityAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid boardId,
        Guid? taskId,
        Guid? criterionId,
        TaskBoardActivityKind kind,
        TaskActor actor,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken,
        string? detail = null)
    {
        await using var command = new NpgsqlCommand(
            $"""
            insert into {this.activityTable}
                (event_id, board_id, task_id, criterion_id, kind,
                 actor_id, actor_kind, occurred_at, detail)
            values (@event, @board, @task, @criterion, @kind, @actor, @actorKind, @occurred, @detail);
            """, connection, transaction);
        command.Parameters.AddWithValue(
            "detail", NpgsqlDbType.Text, string.IsNullOrWhiteSpace(detail) ? DBNull.Value : detail.Trim());
        command.Parameters.AddWithValue("event", Guid.NewGuid());
        command.Parameters.AddWithValue("board", boardId);
        command.Parameters.AddWithValue("task", (object?)taskId ?? DBNull.Value);
        command.Parameters.AddWithValue("criterion", (object?)criterionId ?? DBNull.Value);
        command.Parameters.AddWithValue("kind", kind.ToString());
        command.Parameters.AddWithValue("actor", actor.ActorId);
        command.Parameters.AddWithValue("actorKind", actor.Kind.ToString());
        command.Parameters.AddWithValue("occurred", occurredAt);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> ExecuteBooleanAsync(
        NpgsqlCommand command,
        CancellationToken cancellationToken)
    {
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is true;
    }

    private static async IAsyncEnumerable<TaskBoardActivity> StreamActivityAsync(
        NpgsqlCommand command,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using (command)
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return ReadActivity(reader);
            }
        }
    }

    private static TaskBoardActivity ReadActivity(NpgsqlDataReader reader)
    {
        return new TaskBoardActivity(
            reader.GetInt64(0), reader.GetGuid(1), reader.GetGuid(2),
            reader.IsDBNull(3) ? null : reader.GetGuid(3),
            reader.IsDBNull(4) ? null : reader.GetGuid(4),
            Enum.Parse<TaskBoardActivityKind>(reader.GetString(5)),
            new TaskActor(reader.GetString(6), Enum.Parse<TaskActorKind>(reader.GetString(7))),
            reader.GetFieldValue<DateTimeOffset>(8),
            reader.IsDBNull(9) ? null : Enum.Parse<TaskBoardStatus>(reader.GetString(9)),
            reader.IsDBNull(10) ? null : Enum.Parse<TaskBoardStatus>(reader.GetString(10)),
            reader.IsDBNull(11) ? null : reader.GetString(11));
    }

    private static async IAsyncEnumerable<TaskBoardSummary> StreamSummariesAsync(
        NpgsqlCommand command,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using (command)
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var updatedAt = await reader.GetFieldValueAsync<DateTimeOffset>(
                    3, cancellationToken).ConfigureAwait(false);
                yield return new TaskBoardSummary(
                    reader.GetGuid(0), reader.GetString(1),
                    Enum.Parse<TaskBoardStatus>(reader.GetString(2)),
                    updatedAt, reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6));
            }
        }
    }

    private async Task<bool> InsertBoardAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TaskBoardDraft draft,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"""
            insert into {this.boardsTable}
                (board_id, title, feature_request, plan, root_ordering,
                 planner_kind, privacy_class, execution_origin,
                 created_by_id, created_by_kind, created_at, updated_at)
            values (@id, @title, @request, @plan, @ordering,
                    @planner, @privacy, @origin,
                    @actor, @kind, @created, @created)
            on conflict (board_id) do nothing;
            """, connection, transaction);
        command.Parameters.AddWithValue("id", draft.BoardId);
        command.Parameters.AddWithValue("title", draft.Title);
        command.Parameters.AddWithValue("request", draft.FeatureRequest);
        command.Parameters.AddWithValue("plan", draft.Plan);
        command.Parameters.AddWithValue("ordering", draft.RootOrdering.ToString());
        AddPlanningContext(command, draft.PlanningContext);
        command.Parameters.AddWithValue("actor", draft.CreatedBy.ActorId);
        command.Parameters.AddWithValue("kind", draft.CreatedBy.Kind.ToString());
        command.Parameters.AddWithValue("created", draft.CreatedAt);
        return await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) == 1;
    }

    private static void AddPlanningContext(
        NpgsqlCommand command,
        TaskBoardPlanningContext? context)
    {
        command.Parameters.Add("planner", NpgsqlDbType.Text).Value =
            (object?)context?.Planner.ToString() ?? DBNull.Value;
        command.Parameters.Add("privacy", NpgsqlDbType.Text).Value =
            (object?)context?.Privacy.ToString() ?? DBNull.Value;
        command.Parameters.Add("origin", NpgsqlDbType.Text).Value =
            (object?)context?.Origin.ToString() ?? DBNull.Value;
    }

    private async Task<TaskBoardSnapshot?> ReadSnapshotAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid boardId,
        CancellationToken cancellationToken)
    {
        var board = await this.ReadBoardAsync(
            connection, transaction, boardId, cancellationToken).ConfigureAwait(false);
        if (board is null)
        {
            return null;
        }

        var rows = await this.ReadTasksAsync(
            connection, transaction, boardId, cancellationToken).ConfigureAwait(false);
        await this.ReadCriteriaAsync(
            connection, transaction, boardId, rows, cancellationToken).ConfigureAwait(false);
        await this.ReadPrerequisitesAsync(
            connection, transaction, boardId, rows, cancellationToken).ConfigureAwait(false);
        return board.WithTasks(TaskTreeBuilder.Build(rows, board.RootOrdering));
    }

    private async Task InsertTasksAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        TaskBoardDraft board,
        IReadOnlyList<TaskDraftNode> nodes,
        CancellationToken cancellationToken)
    {
        foreach (var node in nodes)
        {
            await this.InsertTaskAsync(
                connection, transaction, board.BoardId, board.CreatedAt, node, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var node in nodes)
        {
            await this.InsertTaskRelationsAsync(
                connection, transaction, board.BoardId, node.Draft, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task InsertTaskAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid boardId,
        DateTimeOffset createdAt,
        TaskDraftNode node,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"""
            insert into {this.tasksTable}
                (task_id, board_id, parent_task_id, title, description, status, priority,
                 position, subtask_ordering, version, created_at, updated_at)
            values (@id, @board, @parent, @title, @description, 'Open', @priority,
                    @position, @ordering, 1, @created, @created);
            """, connection, transaction);
        command.Parameters.AddWithValue("id", node.Draft.TaskId);
        command.Parameters.AddWithValue("board", boardId);
        command.Parameters.AddWithValue("parent", (object?)node.ParentTaskId ?? DBNull.Value);
        command.Parameters.AddWithValue("title", node.Draft.Title);
        command.Parameters.AddWithValue("description", node.Draft.Description);
        command.Parameters.AddWithValue("priority", (short)node.Draft.Priority);
        command.Parameters.AddWithValue("position", node.Draft.Position);
        command.Parameters.AddWithValue("ordering", node.Draft.SubTaskOrdering.ToString());
        command.Parameters.AddWithValue("created", createdAt);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task InsertTaskRelationsAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid boardId,
        BoardTaskDraft draft,
        CancellationToken cancellationToken)
    {
        foreach (var criterion in draft.AcceptanceCriteria)
        {
            await this.InsertCriterionAsync(
                connection, transaction, draft.TaskId, criterion, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var prerequisiteId in draft.PrerequisiteTaskIds)
        {
            await this.InsertPrerequisiteAsync(
                connection, transaction, boardId, draft.TaskId, prerequisiteId,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task InsertCriterionAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid taskId,
        AcceptanceCriterionDraft criterion,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"""
            insert into {this.criteriaTable}
                (criterion_id, task_id, description, position)
            values (@id, @task, @description, @position);
            """, connection, transaction);
        command.Parameters.AddWithValue("id", criterion.CriterionId);
        command.Parameters.AddWithValue("task", taskId);
        command.Parameters.AddWithValue("description", criterion.Description);
        command.Parameters.AddWithValue("position", criterion.Position);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task InsertPrerequisiteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid boardId,
        Guid taskId,
        Guid prerequisiteId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"""
            insert into {this.prerequisitesTable}
                (board_id, task_id, prerequisite_task_id)
            values (@board, @task, @prerequisite);
            """, connection, transaction);
        command.Parameters.AddWithValue("board", boardId);
        command.Parameters.AddWithValue("task", taskId);
        command.Parameters.AddWithValue("prerequisite", prerequisiteId);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<BoardRow?> ReadBoardAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid boardId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"""
            select board_id, title, feature_request, plan, root_ordering,
                   planner_kind, privacy_class, execution_origin,
                   created_by_id, created_by_kind, created_at, updated_at
              from {this.boardsTable}
             where board_id = @id;
            """, connection, transaction);
        command.Parameters.AddWithValue("id", boardId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        return await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
            ? BoardRow.Read(reader)
            : null;
    }

    private async Task<Dictionary<Guid, TaskRow>> ReadTasksAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid boardId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"""
            select task_id, parent_task_id, title, description, status, priority,
                   position, subtask_ordering, claimed_by_id, claimed_by_kind,
                   claimed_at, version, created_at
              from {this.tasksTable}
             where board_id = @board;
            """, connection, transaction);
        command.Parameters.AddWithValue("board", boardId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        var rows = new Dictionary<Guid, TaskRow>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var row = TaskRow.Read(reader);
            rows.Add(row.TaskId, row);
        }

        return rows;
    }

    private async Task ReadCriteriaAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid boardId,
        IReadOnlyDictionary<Guid, TaskRow> rows,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"""
            select criterion_id, task_id, description, position, is_satisfied,
                   satisfied_by_id, satisfied_by_kind, satisfied_at
              from {this.criteriaTable}
             where task_id in (select task_id from {this.tasksTable} where board_id = @board)
             order by task_id, position, criterion_id;
            """, connection, transaction);
        command.Parameters.AddWithValue("board", boardId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows[reader.GetGuid(1)].Criteria.Add(ReadCriterion(reader));
        }
    }

    private async Task ReadPrerequisitesAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid boardId,
        IReadOnlyDictionary<Guid, TaskRow> rows,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            $"""
            select task_id, prerequisite_task_id
              from {this.prerequisitesTable}
             where board_id = @board
             order by task_id, prerequisite_task_id;
            """, connection, transaction);
        command.Parameters.AddWithValue("board", boardId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows[reader.GetGuid(0)].Prerequisites.Add(reader.GetGuid(1));
        }
    }

    private static AcceptanceCriterion ReadCriterion(NpgsqlDataReader reader)
    {
        TaskActor? actor = reader.IsDBNull(5)
            ? null
            : new TaskActor(reader.GetString(5), Enum.Parse<TaskActorKind>(reader.GetString(6)));
        return new AcceptanceCriterion(
            reader.GetGuid(0), reader.GetString(2), reader.GetInt32(3), reader.GetBoolean(4),
            actor, reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7));
    }
}
