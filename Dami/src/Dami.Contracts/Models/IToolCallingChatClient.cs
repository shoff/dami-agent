namespace Dami.Contracts.Models;

/// <summary>Provider-neutral structured tool-calling conversation.</summary>
public interface IToolCallingChatClient
{
    /// <summary>Returns the next final answer or tool call for the conversation.</summary>
    Task<ToolModelTurn> NextAsync(
        string prompt,
        IReadOnlyList<string> toolSchemas,
        IReadOnlyList<ToolExecutionExchange> exchanges,
        CancellationToken cancellationToken);
}
