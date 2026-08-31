using Dami.Proactive.Releases;

namespace Dami.Proactive.Security;

/// <summary>Evaluates advisory version ranges like "&gt;= 3.0.0, &lt; 3.1.2". Pure.</summary>
/// <remarks>
/// Fails safe-side false: a clause it cannot read yields no match rather than a
/// fabricated alert. The cost is a possible false negative on an exotic range, accepted
/// and stated here rather than hidden.
/// </remarks>
public static class VersionRanges
{
    /// <summary>Whether the version satisfies every clause of the range.</summary>
    public static bool Matches(string version, string range)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(range);

        foreach (var clause in range.Split(','))
        {
            if (!Clause(version, clause.Trim()))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Clause(string version, string clause)
    {
        var (op, operand) = clause switch
        {
            _ when clause.StartsWith("<=", StringComparison.Ordinal) => ("<=", clause[2..].Trim()),
            _ when clause.StartsWith(">=", StringComparison.Ordinal) => (">=", clause[2..].Trim()),
            _ when clause.StartsWith("<", StringComparison.Ordinal) => ("<", clause[1..].Trim()),
            _ when clause.StartsWith(">", StringComparison.Ordinal) => (">", clause[1..].Trim()),
            _ when clause.StartsWith("=", StringComparison.Ordinal) => ("=", clause[1..].Trim()),
            _ => ("", string.Empty),
        };
        if (operand.Length == 0)
        {
            return false;
        }

        var comparison = ReleaseVersions.Compare(version, operand);
        return op switch
        {
            "<" => comparison < 0,
            "<=" => comparison <= 0,
            ">" => comparison > 0,
            ">=" => comparison >= 0,
            "=" => comparison == 0,
            _ => false,
        };
    }
}
