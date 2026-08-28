using System.Globalization;
using Avalonia.Data.Converters;

namespace Dami.Gui;

/// <summary>Turns a bound collection count into "show the placeholder".</summary>
/// <remarks>
/// Every panel in this window can legitimately be empty — no conversation yet, nothing
/// wanting attention, a board with no tasks. Left bare those panels render as blank
/// rectangles that look broken rather than idle, so each one overlays a line of text
/// bound through this converter. <c>ObservableCollection</c> raises a change
/// notification for <c>Count</c>, so the placeholder appears and disappears on its own.
/// </remarks>
public sealed class IsEmpty : IValueConverter
{
    /// <summary>The single instance the XAML binds to.</summary>
    public static readonly IsEmpty instance = new();

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is not int count || count == 0;
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("Emptiness is derived from the collection, never set.");
    }
}
