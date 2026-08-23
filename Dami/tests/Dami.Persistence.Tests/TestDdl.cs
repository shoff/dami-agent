namespace Dami.Persistence.Tests;

/// <summary>Reads the repository's DDL and retargets it at a throwaway schema.</summary>
public static class TestDdl
{
    private static readonly string[] ddlFiles = ["002_event_store.sql", "003_memory.sql", "006_surfacings.sql"];

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

        return $"""
            drop table if exists {schema}.surfacings cascade;
            drop table if exists {schema}.conclusion_observations cascade;
            drop table if exists {schema}.conclusions cascade;
            drop table if exists {schema}.pushbacks cascade;
            drop table if exists {schema}.observations cascade;
            drop table if exists {schema}.execution_events cascade;
            drop function if exists {schema}.reject_mutation() cascade;
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
        // guard dropped deliberately. That the fixture has to do this is the guarantee
        // working, not a workaround for it.
        return $"""
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
