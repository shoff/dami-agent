namespace Dami.Analyzers;

/// <summary>
/// Identifiers for the rules docs/csharpcodestandards.md declares but no shipped
/// analyzer enforces.
/// </summary>
public static class DiagnosticIds
{
    /// <summary>#region is banned (§3).</summary>
    public const string NO_REGIONS = "DAMI0001";

    /// <summary>dynamic is banned (§5).</summary>
    public const string NO_DYNAMIC = "DAMI0002";

    /// <summary>Methods are 30 lines or fewer (§3).</summary>
    public const string METHOD_TOO_LONG = "DAMI0003";

    /// <summary>Loops nest no more than two deep (§3).</summary>
    public const string LOOP_NESTING_TOO_DEEP = "DAMI0004";

    /// <summary>Constructor dependencies are required, never optional (§5).</summary>
    public const string OPTIONAL_CONSTRUCTOR_PARAMETER = "DAMI0005";

    /// <summary>NotImplementedException on an interface implementation breaks LSP (§5).</summary>
    public const string NOT_IMPLEMENTED_MEMBER = "DAMI0006";

    /// <summary>The analyzer category all Dami rules report under.</summary>
    public const string CATEGORY = "Dami";
}
