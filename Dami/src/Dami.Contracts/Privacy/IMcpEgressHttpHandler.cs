namespace Dami.Contracts.Privacy;

/// <summary>Marks the dedicated policy-enforcing HTTP handler authorized for remote MCP.</summary>
/// <remarks>
/// Implementations must enforce privacy classification, destination policy, budgets,
/// request and response bounds, and durable egress events before network I/O.
/// </remarks>
public interface IMcpEgressHttpHandler
{
}
