using Dami.Proactive.Recalls;
using Xunit;

namespace Dami.Proactive.Tests.Recalls;

public sealed class RecallsTests
{
    private const string OPENFDA = """
        { "results": [
            { "classification": "Class I",
              "product_description": "Warfarin Sodium Tablets, 5 mg, 100-count bottle",
              "reason_for_recall": "Tablets may be super potent",
              "recall_initiation_date": "20260815",
              "recalling_firm": "Example Pharma",
              "recall_number": "D-001-2026" } ] }
        """;

    private const string CPSC = """
        [ { "RecallID": 9999, "RecallNumber": "26-123",
            "RecallDate": "2026-08-14T00:00:00",
            "Title": "Example Tool Co. Recalls Angle Grinders Due to Laceration Hazard",
            "Description": "The guard can detach during use.",
            "URL": "https://www.cpsc.gov/Recalls/2026/example",
            "Products": [ { "Name": "9-inch angle grinder", "Type": "Power Tools" } ] } ]
        """;

    [Fact]
    public void ParseOpenFda_Should_Read_A_Recall_With_Its_Date_And_Class()
    {
        var notices = RecallFeeds.ParseOpenFda(OPENFDA, "drug");

        var notice = Assert.Single(notices);
        Assert.Equal(
            ("drug", "Class I", new DateOnly(2026, 8, 15), "D-001-2026"),
            (notice.Source, notice.Classification, notice.Date, notice.Reference));
    }

    [Fact]
    public void ParseOpenFda_Should_Yield_Nothing_For_Garbage()
    {
        Assert.Empty(RecallFeeds.ParseOpenFda("not json", "drug"));
    }

    [Fact]
    public void ParseCpsc_Should_Read_Title_Products_And_Link()
    {
        var notices = RecallFeeds.ParseCpsc(CPSC);

        var notice = Assert.Single(notices);
        Assert.Equal(
            (true, "https://www.cpsc.gov/Recalls/2026/example", new DateOnly(2026, 8, 14)),
            (notice.Product.Contains("angle grinder", StringComparison.OrdinalIgnoreCase),
                notice.Reference, notice.Date));
    }
}
