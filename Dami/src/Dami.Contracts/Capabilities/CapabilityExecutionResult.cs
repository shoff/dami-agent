using System.Collections.ObjectModel;

namespace Dami.Contracts.Capabilities;

/// <summary>A successful capability output backed by verifiable evidence.</summary>
public sealed class CapabilityExecutionResult
{
    /// <summary>Creates a successful result and snapshots its evidence.</summary>
    public CapabilityExecutionResult(string output, IReadOnlyDictionary<string, string> evidence)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(evidence);
        if (evidence.Count == 0)
        {
            throw new ArgumentException("A successful capability result requires evidence.", nameof(evidence));
        }

        this.Output = output;
        this.Evidence = new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(evidence, StringComparer.Ordinal));
    }

    /// <summary>Gets the output suitable for returning to the requesting model.</summary>
    public string Output { get; }

    /// <summary>Gets immutable evidence supporting the reported success.</summary>
    public IReadOnlyDictionary<string, string> Evidence { get; }
}
