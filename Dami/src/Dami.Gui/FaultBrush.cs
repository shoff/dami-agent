using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Dami.Gui;

/// <summary>Colours a network row: red for a fault, green for a healthy fact.</summary>
public sealed class FaultBrush : IValueConverter
{
    /// <summary>The single instance the XAML binds to.</summary>
    public static readonly FaultBrush instance = new();

    private static readonly SolidColorBrush fault = new(Color.Parse("#E0604F"));
    private static readonly SolidColorBrush healthy = new(Color.Parse("#4CB782"));

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? fault : healthy;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("Fault colours are display-only.");
    }
}
