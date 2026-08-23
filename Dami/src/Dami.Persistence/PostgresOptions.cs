namespace Dami.Persistence;

/// <summary>Configuration shared by every Postgres store.</summary>
public sealed class PostgresOptions
{
    /// <summary>Configuration section these bind from.</summary>
    public const string SECTION_NAME = "Postgres";

    /// <summary>
    /// The schema every store qualifies its tables with.
    /// </summary>
    /// <remarks>
    /// Never hardcode the schema in a store. Parameterising it is what lets integration
    /// tests run against a throwaway schema holding the real DDL, rather than against
    /// the live one.
    /// </remarks>
    public string SchemaName { get; set; } = "dami";
}
