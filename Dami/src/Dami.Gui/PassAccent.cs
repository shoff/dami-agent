using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Dami.Gui;

/// <summary>Colours a replayed pass event by what kind of thing it was.</summary>
/// <remarks>
/// A pass reads as a wall of near-identical rows unless the shape of it is visible at a
/// glance: what reached the network, what the pass produced, and what went wrong. Type,
/// not status, drives this — an egress that completed with a 429 is a "Completed" event
/// and still the most important line in the trace, so alerts are decided separately and
/// win.
/// </remarks>
public sealed class PassAccent : IValueConverter
{
    /// <summary>The single instance the XAML binds to.</summary>
    public static readonly PassAccent instance = new();

    private static readonly SolidColorBrush produced = new(Color.Parse("#4CB782"));
    private static readonly SolidColorBrush egress = new(Color.Parse("#5AA9E6"));
    private static readonly SolidColorBrush boundary = new(Color.Parse("#7A8694"));
    private static readonly SolidColorBrush alert = new(Color.Parse("#E0604F"));

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not PassEvent item)
        {
            return boundary;
        }

        if (item.IsAlert)
        {
            return alert;
        }

        return item.Type switch
        {
            var type when type.StartsWith("Egress", StringComparison.Ordinal) => egress,
            "Surfaced" or "Concluded" or "Observed" or "FactRecorded" => produced,
            _ => boundary,
        };
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("Accents are display-only.");
    }
}
