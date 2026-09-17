using Dami.Contracts.Research;
using Xunit;

namespace Dami.Gui.Tests;

public sealed class ResearchPresentationTests
{
    [Theory]
    [InlineData("", "02  api.weather.gov/openapi.json")]
    [InlineData("   ", "02  api.weather.gov/openapi.json")]
    [InlineData("API schema", "02  API schema")]
    public void Source_Label_Should_Fall_Back_To_The_URL_When_An_API_Response_Has_No_Title(string title, string expected)
    {
        var url = new Uri("https://api.weather.gov/openapi.json");
        var row = new ResearchSourceRow(new ResearchFinding(new ResearchPage(url, 200, title, "{}"), 1, [url]), 1);
        Assert.Equal(expected, row.Title);
    }

    [Theory]
    [InlineData(ResearchAnswerStatus.Complete, "Saved answer")]
    [InlineData(ResearchAnswerStatus.Partial, "Partial answer")]
    [InlineData(ResearchAnswerStatus.None, "No saved answer")]
    public void Export_Should_Keep_Evidence_Paths_And_Explain_An_Incomplete_Answer(ResearchAnswerStatus status, string label)
    {
        var seed = new Uri("https://public.example/start");
        var source = new Uri("https://public.example/data");
        var run = new ResearchRun(Guid.NewGuid(), Guid.NewGuid(), "Which data is available?", seed,
            DateTimeOffset.Parse("2026-09-11T12:00:00Z"))
        {
            Status = ResearchRunStatus.Completed, AnswerStatus = status,
            Answer = status == ResearchAnswerStatus.None ? null : "The source reports 417 permits.",
            PageLimitReached = true,
            Findings = [new ResearchFinding(new ResearchPage(source, 200, "Permit data", "Original source: 417"), 1, [seed, source])],
            Issues = [new ResearchIssue(new Uri("https://public.example/report.pdf"), "Unsupported PDF")],
        };
        var report = ResearchPresentation.Export(run);
        Assert.Contains(label, report, StringComparison.Ordinal);
        Assert.Contains("Original source: 417", report, StringComparison.Ordinal);
        Assert.Contains("https://public.example/start → https://public.example/data", report, StringComparison.Ordinal);
        Assert.Contains("Unsupported PDF", report, StringComparison.Ordinal);
        Assert.Contains("Page limit reached", report, StringComparison.Ordinal);
    }
}
