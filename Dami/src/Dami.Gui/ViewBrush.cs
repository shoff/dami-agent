using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Dami.Gui;

/// <summary>Highlights whichever board-view button is the active one.</summary>
/// <remarks>
/// The filter bar is only useful if you can see which slice you are looking at; four
/// identical buttons tell you nothing. The converter takes the panel's current
/// <see cref="BoardView"/> as the value and the button's own view name as the parameter.
/// </remarks>
public sealed class ViewBrush : IValueConverter
{
    /// <summary>The single instance the XAML binds to.</summary>
    public static readonly ViewBrush instance = new();

    private static readonly SolidColorBrush selected = new(Color.Parse("#2F4A66"));
    private static readonly SolidColorBrush idle = new(Color.Parse("#1B2229"));

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var active = value is BoardView view
            && string.Equals(view.ToString(), parameter as string, StringComparison.Ordinal);
        return active ? selected : idle;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("The active view is chosen by clicking, never by the brush.");
    }
}
