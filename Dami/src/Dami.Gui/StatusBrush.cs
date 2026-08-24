using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Dami.Gui;

/// <summary>Colours a graph row by the status the runtime persisted.</summary>
public sealed class StatusBrush : IValueConverter
{
    /// <summary>The single instance the XAML binds to.</summary>
    public static readonly StatusBrush instance = new();

    private static readonly SolidColorBrush succeeded = new(Color.Parse("#4CB782"));
    private static readonly SolidColorBrush running = new(Color.Parse("#D9A441"));
    private static readonly SolidColorBrush failed = new(Color.Parse("#E0604F"));
    private static readonly SolidColorBrush other = new(Color.Parse("#7A8694"));

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (value as string) switch
        {
            "Succeeded" => succeeded,
            "Running" => running,
            "Failed" => failed,
            _ => other,
        };
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("Status colours are display-only.");
    }
}
