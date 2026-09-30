using Dami.Core.Anticoag;
using Xunit;

namespace Dami.Core.Tests.Anticoag;

/// <summary>
/// What in Steve's words is a reading, a dose change or an interacting drug (ADR-0037). A
/// false positive logs a wrong number beside his real ones, so the patterns are narrow.
/// </summary>
public sealed class AnticoagCaptureTests
{
    private static readonly DateOnly today = new(2026, 9, 29);

    [Theory]
    [InlineData("INR 2.4", 2.4, 0)]
    [InlineData("inr was 2.8 today", 2.8, 0)]
    [InlineData("INR: 3.1", 3.1, 0)]
    [InlineData("INR came back 1.9 yesterday", 1.9, -1)]
    [InlineData("my INR is 2.5", 2.5, 0)]
    [InlineData("INR 3", 3.0, 0)]
    public void A_Reported_Reading_Should_Be_Read(string text, double inr, int daysBack)
    {
        var reading = AnticoagCapture.Reading(text, today);

        Assert.Equal(((decimal)inr, today.AddDays(daysBack)), reading);
    }

    [Theory]
    [InlineData("what's a normal INR 2.0?")]
    [InlineData("my target INR is 2.0 to 3.0")]
    [InlineData("INR range 2.0-3.0")]
    [InlineData("INR 25")]
    [InlineData("the INR clinic called")]
    [InlineData("should my INR be 2.5")]
    public void Questions_Ranges_And_Nonsense_Should_Not_Be_Readings(string text)
    {
        Assert.Null(AnticoagCapture.Reading(text, today));
    }

    [Theory]
    [InlineData("clinic changed me to 7.5 mg Mon/Wed, 5 mg other days")]
    [InlineData("new warfarin dose: 6mg daily")]
    [InlineData("they lowered my coumadin to 4 mg")]
    public void A_Reported_Dose_Change_Should_Be_Kept_Verbatim(string text)
    {
        Assert.Equal(text, AnticoagCapture.DoseChange(text));
    }

    [Theory]
    [InlineData("took my 5mg warfarin")]
    [InlineData("should I change my warfarin dose to 6 mg?")]
    [InlineData("changed the oil, 5 quarts")]
    public void Taking_Asking_Or_Unrelated_Should_Not_Be_A_Dose_Change(string text)
    {
        Assert.Null(AnticoagCapture.DoseChange(text));
    }

    [Theory]
    [InlineData("doc put me on Bactrim for a UTI", "Bactrim")]
    [InlineData("taking some ibuprofen for my back", "ibuprofen")]
    [InlineData("started St. John's Wort", "St John's wort")]
    [InlineData("they gave me an antibiotic", "an antibiotic")]
    [InlineData("rifampin again", "rifampin")]
    public void A_Known_Interactor_Should_Be_Named(string text, string named)
    {
        Assert.Equal(named, AnticoagCapture.Interactor(text)?.Name);
    }

    [Fact]
    public void Ordinary_Talk_Should_Name_No_Interactor()
    {
        Assert.Null(AnticoagCapture.Interactor("went for a run and had a salad"));
    }
}
