namespace Dami.Gui;

/// <summary>
/// A file the client can always write to. Avalonia desktop apps do not reliably keep a
/// usable stdout, so a diagnostic that only reaches the console is a diagnostic that
/// silently does not exist — which is how a dead poll loop looked like an idle system.
/// </summary>
public static class Diagnostics
{
    private static readonly string path = Path.Combine(Path.GetTempPath(), "dami-gui.log");
    private static readonly Lock gate = new();

    /// <summary>Appends one timestamped line. Never throws; diagnostics must not break the app.</summary>
    /// <param name="message">What happened.</param>
    /// <param name="clock">The clock, because ambient time is banned for good reasons.</param>
    public static void Write(string message, TimeProvider? clock = null)
    {
        var now = (clock ?? TimeProvider.System).GetLocalNow();
        try
        {
            lock (gate)
            {
                File.AppendAllText(path, $"{now:HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch (IOException)
        {
            // A diagnostic that cannot be written is not worth failing a turn over.
        }
    }
}
