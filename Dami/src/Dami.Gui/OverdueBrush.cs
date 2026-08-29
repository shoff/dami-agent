using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Dami.Gui;

/// <summary>Marks a service that has missed its cadence.</summary>
/// <remarks>
/// The distinction this draws is the one the workers view exists for. A service last seen
/// five days ago is healthy on a weekly cadence and broken on a nightly one, and until the
/// cadence was recorded alongside the run there was no way to tell them apart from any
/// interface.
/// </remarks>
public sealed class OverdueBrush : IValueConverter
{
    /// <summary>The single instance the XAML binds to.</summary>
    public static readonly OverdueBrush instance = new();

    private static readonly SolidColorBrush overdue = new(Color.Parse("#E0604F"));
    private static readonly SolidColorBrush onSchedule = new(Color.Parse("#7A8694"));

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? overdue : onSchedule;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("Lateness is derived from the schedule, never set.");
    }
}
