using System.Collections.ObjectModel;
using System.Globalization;
using Xunit;

namespace Dami.Gui.Tests;

public sealed class IsEmptyTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(42, false)]
    public void Convert_Should_Report_Empty_Only_For_A_Zero_Count(int count, bool expected)
    {
        var actual = IsEmpty.instance.Convert(
            count, typeof(bool), null, CultureInfo.InvariantCulture);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Convert_Should_Treat_An_Unbound_Count_As_Empty()
    {
        // A binding that has not resolved yet sends null. Showing the placeholder is the
        // honest answer: nothing has arrived, so nothing is on screen.
        var actual = IsEmpty.instance.Convert(
            null, typeof(bool), null, CultureInfo.InvariantCulture);

        Assert.Equal(true, actual);
    }

    [Fact]
    public void Convert_Should_Follow_An_Observable_Collection_As_It_Fills()
    {
        // The panels bind to Collection.Count, so the converter has to agree with what
        // ObservableCollection reports before and after a mutation.
        var items = new ObservableCollection<string>();
        Assert.Equal(
            true, IsEmpty.instance.Convert(items.Count, typeof(bool), null, CultureInfo.InvariantCulture));

        items.Add("first");

        Assert.Equal(
            false, IsEmpty.instance.Convert(items.Count, typeof(bool), null, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ConvertBack_Should_Refuse()
    {
        Assert.Throws<NotSupportedException>(() => IsEmpty.instance.ConvertBack(
            true, typeof(int), null, CultureInfo.InvariantCulture));
    }
}
