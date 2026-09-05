namespace Dami.Gui;

/// <summary>One piece of a streamed reply: text, or a picture the runtime kept in the Gallery.</summary>
public sealed record StreamedFragment(string? Text, string? PictureFileName)
{
    /// <summary>Text to append to the reply.</summary>
    public static StreamedFragment OfText(string text) => new(text, null);

    /// <summary>A Gallery file to show with the reply.</summary>
    public static StreamedFragment OfPicture(string fileName) => new(null, fileName);
}

/// <summary>The runtime's event-stream grammar, read line by line. Pure over a reader.</summary>
/// <remarks>
/// Plain <c>data:</c> blocks are text; a block introduced by <c>event: picture</c> names a
/// Gallery file (ADR-0030). Anything else is ignored rather than shown.
/// </remarks>
public static class ServerSentEvents
{
    private const string DATA = "data: ";
    private const string EVENT = "event: ";

    /// <summary>Reads fragments until the stream ends.</summary>
    public static async IAsyncEnumerable<StreamedFragment> ReadAsync(
        TextReader reader,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var pending = new List<string>();
        string? eventName = null;
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (line.StartsWith(EVENT, StringComparison.Ordinal))
            {
                eventName = line[EVENT.Length..].Trim();
            }
            else if (line.StartsWith(DATA, StringComparison.Ordinal))
            {
                pending.Add(line[DATA.Length..]);
            }
            else if (line.Length == 0 && pending.Count > 0)
            {
                if (Block(eventName, string.Join('\n', pending)) is { } fragment)
                {
                    yield return fragment;
                }

                pending.Clear();
                eventName = null;
            }
        }
    }

    private static StreamedFragment? Block(string? eventName, string data) =>
        eventName switch
        {
            null => StreamedFragment.OfText(data),
            "picture" => StreamedFragment.OfPicture(data.Trim()),
            _ => null,
        };
}
