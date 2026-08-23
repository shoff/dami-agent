namespace Dami.Core.Turns;

/// <summary>Runs one interactive turn: context → route → model → traced answer.</summary>
public interface ITurnRunner
{
    /// <summary>Answers a request, leaving a complete trace behind.</summary>
    Task<TurnResult> RunAsync(string request, CancellationToken cancellationToken);

    /// <summary>Begins a streaming turn: context and route now, tokens as they arrive.</summary>
    /// <remarks>
    /// The trace completes — and the interaction joins the corpus — when the token
    /// stream is drained. An undrained stream is an unfinished turn.
    /// </remarks>
    Task<TurnStream> BeginStreamingAsync(string request, CancellationToken cancellationToken);
}
