using Dami.Proactive.Recalls;
using Xunit;

namespace Dami.Proactive.Tests.Recalls;

public sealed class RecallTermsTests
{
    [Fact]
    public void FromMedications_Should_Keep_Drug_Names_And_Drop_Dosing_Words()
    {
        var terms = RecallTerms.FromMedications(
            ["Started metoprolol 25 mg daily", "Warfarin discontinued after surgery"]);

        Assert.Equal(
            (true, true, false, false),
            (terms.Contains("metoprolol"), terms.Contains("warfarin"),
                terms.Contains("daily"), terms.Contains("started")));
    }

    [Fact]
    public void Mentions_Should_Match_A_Term_Inside_A_Product_Description()
    {
        Assert.Equal(
            "warfarin",
            RecallTerms.Mentions("Warfarin Sodium Tablets, 5 mg", ["warfarin", "lisinopril"]));
    }

    [Fact]
    public void Mentions_Should_Match_A_Multi_Word_Watch_Term()
    {
        Assert.Equal(
            "aortic valve",
            RecallTerms.Mentions(
                "Model X mechanical aortic valve — fatigue fracture of the leaflet",
                ["aortic valve"]));
    }

    [Fact]
    public void Mentions_Should_Stay_Quiet_When_Nothing_Matches()
    {
        Assert.Null(RecallTerms.Mentions("Ibuprofen 200 mg caplets", ["warfarin", "aortic valve"]));
    }
}
