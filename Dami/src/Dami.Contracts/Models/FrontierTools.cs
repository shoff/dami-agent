using System.Text.Json;

namespace Dami.Contracts.Models;

/// <summary>One function the frontier may call during a turn.</summary>
/// <param name="Name">The name the model calls it by.</param>
/// <param name="Description">What it does and what comes back, written for the model.</param>
/// <param name="InputSchema">A JSON Schema object describing the arguments.</param>
public sealed record FrontierTool(string Name, string Description, JsonElement InputSchema);

/// <summary>A call the frontier made: which tool, with what arguments.</summary>
public sealed record FrontierToolCall(string CallId, string Tool, JsonElement Arguments);

/// <summary>What the runtime hands back to the frontier for one call.</summary>
public sealed record FrontierToolResult(bool Success, string Text)
{
    /// <summary>A result the model can build on.</summary>
    public static FrontierToolResult Ok(string text) => new(true, text);

    /// <summary>A failure the model is told about, in words rather than a stack.</summary>
    public static FrontierToolResult Failed(string reason) => new(false, reason);
}

/// <summary>Runs a tool call on this host while the frontier waits.</summary>
public interface IFrontierToolHandler
{
    /// <summary>Executes one call. Must return rather than throw: the turn is waiting.</summary>
    Task<FrontierToolResult> HandleAsync(FrontierToolCall call, CancellationToken cancellationToken);
}

/// <summary>The tools offered to one frontier turn, and who answers them.</summary>
/// <remarks>
/// Per turn, deliberately: the capability router's job is to pick a small bundle for
/// the turn at hand, and a bundle carries state — the pictures it made, for instance —
/// that belongs to that turn and no other.
/// </remarks>
public sealed class FrontierToolbox
{
    /// <summary>No tools; the frontier answers with words alone.</summary>
    public static FrontierToolbox Empty { get; } = new([], new NoTools());

    /// <summary>Creates a toolbox.</summary>
    public FrontierToolbox(IReadOnlyList<FrontierTool> tools, IFrontierToolHandler handler)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(handler);
        this.Tools = tools;
        this.Handler = handler;
    }

    /// <summary>What the model may call.</summary>
    public IReadOnlyList<FrontierTool> Tools { get; }

    /// <summary>Who runs the calls.</summary>
    public IFrontierToolHandler Handler { get; }

    /// <summary>Whether anything is offered at all.</summary>
    public bool IsEmpty => this.Tools.Count == 0;

    private sealed class NoTools : IFrontierToolHandler
    {
        public Task<FrontierToolResult> HandleAsync(FrontierToolCall call, CancellationToken cancellationToken) =>
            Task.FromResult(FrontierToolResult.Failed($"no tool named {call.Tool} was offered"));
    }
}
