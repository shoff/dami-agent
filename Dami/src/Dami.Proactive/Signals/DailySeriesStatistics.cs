using Dami.Contracts.Domains;

namespace Dami.Proactive.Signals;

/// <summary>The little arithmetic the signals need, written out so it can be read.</summary>
public static class DailySeriesStatistics
{
    /// <summary>The median of the values; the middle pair averaged for an even count.</summary>
    public static double Median(IReadOnlyList<double> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count == 0)
        {
            throw new ArgumentException("A median needs at least one value.", nameof(values));
        }

        var sorted = new double[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            sorted[index] = values[index];
        }

        Array.Sort(sorted);
        var middle = sorted.Length / 2;
        return sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2.0;
    }

    /// <summary>Pearson's r, or null when either side never moves or there are fewer than two pairs.</summary>
    public static double? Pearson(double[] x, double[] y)
    {
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);
        if (x.Length != y.Length || x.Length < 2)
        {
            return null;
        }

        var meanX = Mean(x);
        var meanY = Mean(y);
        double covariance = 0, varianceX = 0, varianceY = 0;
        for (var index = 0; index < x.Length; index++)
        {
            var dx = x[index] - meanX;
            var dy = y[index] - meanY;
            covariance += dx * dy;
            varianceX += dx * dx;
            varianceY += dy * dy;
        }

        return varianceX == 0 || varianceY == 0 ? null : covariance / Math.Sqrt(varianceX * varianceY);
    }

    /// <summary>
    /// Two series as dense, aligned arrays over the window, missing days as zero, the
    /// second shifted forward by <paramref name="lag"/> days so x on day d pairs with y on
    /// day d + lag.
    /// </summary>
    public static (double[] X, double[] Y) Align(DailySeries first, DailySeries second, DateOnly from, DateOnly to, int lag)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);
        var days = to.DayNumber - from.DayNumber + 1;
        var length = Math.Max(0, days - lag);
        var x = new double[length];
        var y = new double[length];
        Fill(x, first, from, 0);
        Fill(y, second, from, lag);
        return (x, y);
    }

    private static void Fill(double[] target, DailySeries series, DateOnly from, int shift)
    {
        foreach (var point in series.Points)
        {
            var index = point.Day.DayNumber - from.DayNumber - shift;
            if (index >= 0 && index < target.Length)
            {
                target[index] = point.Value;
            }
        }
    }

    private static double Mean(double[] values)
    {
        double sum = 0;
        foreach (var value in values)
        {
            sum += value;
        }

        return sum / values.Length;
    }
}
