namespace Dami.Core.Turns;

/// <summary>Runs one interactive turn: context → route → model → traced answer.</summary>
public interface ITurnRunner
{
    /// <summary>Answers a request, leaving a complete trace behind.</summary>
    Task<TurnResult> RunAsync(string request, CancellationToken cancellationToken);
}
