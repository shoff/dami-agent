namespace Dami.Persistence.Tests;

/// <summary>Reads the repository's DDL and retargets it at a throwaway schema.</summary>
public static class TestDdl
{
    private static readonly string[] ddlFiles =
    [
        "002_event_store.sql",
        "003_memory.sql",
        "006_surfacings.sql",
        "007_proactive_runs.sql",
        "008_observation_embeddings.sql",
        "009_approvals.sql",
        "010_conclusion_embeddings.sql",
        "011_capability_embeddings.sql",
        "012_observation_date_repairs.sql",
        "013_egress_briefs.sql",
        "014_health_events.sql",
        "015_health_examined.sql",
        "016_file_patch_proposals.sql",
        "017_file_patch_proposal_privileges.sql",
        "018_approval_trace_provenance.sql",
        "019_conversation_sessions.sql",
        "020_skill_changes.sql",
        "021_skill_change_recovery.sql",
        "022_tool_proposals.sql",
        "023_tool_promotions.sql",
        "024_tool_activation_state.sql",
        "025_tool_activation_advisory_lock.sql",
        "028_task_boards.sql",
        "009_versioned_embeddings.sql",
        "010_proactive_run_leases.sql",
        "017_gateway_authority.sql",
        "018_health_event_rejections.sql",
        "019_briefs_without_approval.sql",
        "020_observation_curations.sql",
    ];

    /// <summary>The event-store and memory DDL, rewritten to build in <paramref name="schema"/>.</summary>
    /// <remarks>
    /// Applied in filename order because 003 depends on the trigger function 002 creates.
    /// </remarks>
    public static string EventStoreForSchema(string schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var directory = FindDdlDirectory();
        var retargeted = ddlFiles
            .Select(file => File.ReadAllText(Path.Combine(directory, file)))
            .Select(source => source.Replace("dami.", $"{schema}.", StringComparison.Ordinal));

        return DropEventStore(schema) + "\n" + string.Join("\n", retargeted);
    }

    /// <summary>Removes the objects the fixture created, leaving the schema itself.</summary>
    /// <remarks>
    /// Objects rather than the schema, deliberately. <c>dami_ddl</c> owns
    /// <c>dami_test</c> but holds no CREATE privilege on the database, so it can rebuild
    /// what is inside the schema and cannot recreate the schema. Keeping it that way is
    /// least privilege working rather than an inconvenience to route around.
    /// </remarks>
    public static string DropEventStore(string schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        return DropTaskBoards(schema) + DropToolStaging(schema) + DropObservationOverlays(schema) + $"""
            drop table if exists {schema}.skill_changes cascade;
            drop table if exists {schema}.conversation_turns cascade;
            drop table if exists {schema}.conversation_sessions cascade;
            drop table if exists {schema}.file_patch_proposals cascade;
            drop table if exists {schema}.health_event_rejections cascade;
            drop table if exists {schema}.gateway_authority cascade;
            drop table if exists {schema}.health_examined cascade;
            drop table if exists {schema}.health_events cascade;
            drop table if exists {schema}.egress_briefs cascade;
            drop table if exists {schema}.capability_embeddings cascade;
            drop table if exists {schema}.conclusion_embeddings cascade;
            drop function if exists {schema}.drop_conclusion_embedding() cascade;
            drop table if exists {schema}.approvals cascade;
            drop table if exists {schema}.observation_embeddings cascade;
            drop table if exists {schema}.proactive_run_leases cascade;
            drop table if exists {schema}.proactive_runs cascade;
            drop table if exists {schema}.surfacings cascade;
            drop table if exists {schema}.conclusion_observations cascade;
            drop table if exists {schema}.conclusions cascade;
            drop table if exists {schema}.pushbacks cascade;
            drop table if exists {schema}.observations cascade;
            drop table if exists {schema}.execution_events cascade;
            drop function if exists {schema}.validate_tool_activation_outcome() cascade;
            drop function if exists {schema}.validate_tool_promotion() cascade;
            drop function if exists {schema}.reject_mutation() cascade;
            """;
    }

    /// <summary>The tables that overlay observations without replacing them.</summary>
    /// <remarks>
    /// Curations and date repairs both sit beside <c>observations</c> rather than editing
    /// it, because the corpus is append-only; the corpus query coalesces over them, so a
    /// schema without them fails every read rather than quietly returning raw bodies.
    /// </remarks>
    private static string DropObservationOverlays(string schema)
    {
        return $"""
            drop table if exists {schema}.observation_curations cascade;
            drop table if exists {schema}.observation_date_repairs cascade;

            """;
    }

    private static string DropTaskBoards(string schema)
    {
        return $"""
            drop function if exists {schema}.task_board_try_claim(uuid, uuid, bigint, text, text, timestamptz);
            drop function if exists {schema}.task_board_try_set_criterion(uuid, uuid, bigint, boolean, text, text, timestamptz);
            drop function if exists {schema}.task_board_try_complete(uuid, uuid, bigint, text, text, timestamptz);
            drop function if exists {schema}.task_board_try_set_status(uuid, uuid, bigint, text, text, text, text, timestamptz);
            drop table if exists {schema}.task_board_activity cascade;
            drop table if exists {schema}.task_prerequisites cascade;
            drop table if exists {schema}.task_acceptance_criteria cascade;
            drop table if exists {schema}.task_board_tasks cascade;
            drop table if exists {schema}.task_boards cascade;

            """;
    }

    private static string TruncateObservationOverlays(string schema)
    {
        return $"""
            delete from {schema}.observation_curations;
            delete from {schema}.observation_date_repairs;

            """;
    }

    private static string DropToolStaging(string schema)
    {
        return $"""
            drop table if exists {schema}.tool_activation_outcomes cascade;
            drop table if exists {schema}.tool_verifications cascade;
            drop table if exists {schema}.tool_promotions cascade;
            drop table if exists {schema}.tool_proposals cascade;

            """;
    }

    /// <summary>
    /// Empties the table between tests.
    /// </summary>
    /// <remarks>
    /// The append-only triggers are dropped and recreated deliberately. That the test
    /// fixture has to do this is the guarantee working, not a workaround for it.
    /// </remarks>
    public static string TruncateEventStore(string schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        // Order matters: children before parents, and the append-only tables need their
        // guard dropped deliberately; that friction is the guarantee working.
        return TruncateTaskBoards(schema) + TruncateSessions(schema) + TruncateToolActivationOutcomes(schema)
            + TruncateToolVerifications(schema)
            + TruncateToolPromotions(schema)
            + TruncateToolProposals(schema) + TruncateSkillChanges(schema)
            + TruncateFilePatchProposals(schema) + TruncateObservationOverlays(schema) + $"""
            delete from {schema}.health_event_rejections;  delete from {schema}.gateway_authority;  delete from {schema}.health_examined;
            delete from {schema}.health_events;
            delete from {schema}.egress_briefs;
            delete from {schema}.capability_embeddings;
            delete from {schema}.conclusion_embeddings;
            delete from {schema}.approvals;
            delete from {schema}.observation_embeddings;
            delete from {schema}.proactive_run_leases;
            delete from {schema}.proactive_runs;
            delete from {schema}.surfacings;
            delete from {schema}.conclusion_observations;
            delete from {schema}.conclusions;
            delete from {schema}.pushbacks;
            alter table {schema}.observations disable trigger observations_append_only;
            delete from {schema}.observations;
            alter table {schema}.observations enable trigger observations_append_only;
            alter table {schema}.execution_events disable trigger execution_events_append_only;
            delete from {schema}.execution_events;
            alter table {schema}.execution_events enable trigger execution_events_append_only;
            """;
    }

    private static string TruncateFilePatchProposals(string schema)
    {
        return $"""
            alter table {schema}.file_patch_proposals disable trigger file_patch_proposals_append_only;
            delete from {schema}.file_patch_proposals;
            alter table {schema}.file_patch_proposals enable trigger file_patch_proposals_append_only;

            """;
    }

    private static string TruncateTaskBoards(string schema)
    {
        return $"""
            alter table {schema}.task_board_activity disable trigger task_board_activity_append_only;
            delete from {schema}.task_board_activity;
            alter table {schema}.task_board_activity enable trigger task_board_activity_append_only;
            delete from {schema}.task_prerequisites;
            delete from {schema}.task_acceptance_criteria;
            delete from {schema}.task_board_tasks;
            delete from {schema}.task_boards;

            """;
    }

    private static string TruncateSkillChanges(string schema)
    {
        return $"""
            alter table {schema}.skill_changes disable trigger skill_changes_append_only;
            delete from {schema}.skill_changes;
            alter table {schema}.skill_changes enable trigger skill_changes_append_only;

            """;
    }

    private static string TruncateToolProposals(string schema)
    {
        return $"""
            alter table {schema}.tool_proposals disable trigger tool_proposals_append_only;
            delete from {schema}.tool_proposals;
            alter table {schema}.tool_proposals enable trigger tool_proposals_append_only;

            """;
    }

    private static string TruncateToolPromotions(string schema)
    {
        return $"""
            alter table {schema}.tool_promotions disable trigger tool_promotions_append_only;
            delete from {schema}.tool_promotions;
            alter table {schema}.tool_promotions enable trigger tool_promotions_append_only;

            """;
    }

    private static string TruncateToolVerifications(string schema)
    {
        return $"""
            alter table {schema}.tool_verifications disable trigger tool_verifications_append_only;
            delete from {schema}.tool_verifications;
            alter table {schema}.tool_verifications enable trigger tool_verifications_append_only;

            """;
    }

    private static string TruncateToolActivationOutcomes(string schema)
    {
        return $"""
            alter table {schema}.tool_activation_outcomes disable trigger tool_activation_outcomes_append_only;
            delete from {schema}.tool_activation_outcomes;
            alter table {schema}.tool_activation_outcomes enable trigger tool_activation_outcomes_append_only;

            """;
    }

    private static string TruncateSessions(string schema)
    {
        return $"""
            delete from {schema}.conversation_turns;
            delete from {schema}.conversation_sessions;

            """;
    }

    private static string FindDdlDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "tools", "ddl");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate tools/ddl above {AppContext.BaseDirectory}. "
            + "These are integration tests and require the repository checkout.");
    }
}
