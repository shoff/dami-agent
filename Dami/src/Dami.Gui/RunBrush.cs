using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Dami.Gui;

/// <summary>Colours one pass by how it actually went.</summary>
/// <remarks>
/// Three outcomes, not two. A pass that failed outright and a pass that completed while
/// being refused by a server are both worth a red block, and everything else is worth a
/// green one — the point of the strip is that a healthy service reads as healthy without
/// being clicked. Neutral grey for the good case would make the whole row say nothing,
/// which is what it did when this reused the overdue converter.
/// </remarks>
public sealed class RunBrush : IValueConverter
{
    /// <summary>The single instance the XAML binds to.</summary>
    public static readonly RunBrush instance = new();

    private static readonly SolidColorBrush healthy = new(Color.Parse("#4CB782"));
    private static readonly SolidColorBrush trouble = new(Color.Parse("#E0604F"));
    private static readonly SolidColorBrush unknown = new(Color.Parse("#7A8694"));

    /// <inheritdoc />
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            WorkerRun run when run.HasAlerts || run.Status is "Failed" or "Cancelled" => trouble,
            WorkerRun => healthy,
            _ => unknown,
        };
    }

    /// <inheritdoc />
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("How a pass went is a fact about it, never a setting.");
    }
}
