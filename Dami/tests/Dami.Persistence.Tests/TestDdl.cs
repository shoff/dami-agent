namespace Dami.Persistence.Tests;

/// <summary>Reads the repository's DDL and retargets it at a throwaway schema.</summary>
public static class TestDdl
{
    private const string DDL_FILE = "002_event_store.sql";

    /// <summary>The event-store DDL, rewritten to build in <paramref name="schema"/>.</summary>
    public static string EventStoreForSchema(string schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        var source = File.ReadAllText(Path.Combine(FindDdlDirectory(), DDL_FILE));
        return DropEventStore(schema) + "\n"
            + source.Replace("dami.", $"{schema}.", StringComparison.Ordinal);
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

        return $"""
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
