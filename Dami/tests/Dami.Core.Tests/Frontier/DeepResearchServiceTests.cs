using Dami.Contracts.Events;
using Dami.Contracts.Privacy;
using Dami.Contracts.Research;
using Dami.Core.Frontier;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace Dami.Core.Tests.Frontier;

public sealed class DeepResearchServiceTests
{
    private const int HARD_PAGE_LIMIT = 25;

    [Theory]
    [InlineData(".png")]
    [InlineData(".ico")]
    [InlineData(".svg")]
    [InlineData(".css")]
    [InlineData(".js")]
    [InlineData(".woff2")]
    public async Task Explore_Should_Not_Spend_Its_Read_Budget_On_Website_Assets(string extension)
    {
        var root = new Uri("https://public.example/");
        var asset = new Uri("https://public.example/api/favicon" + extension);
        var evidence = new Uri("https://public.example/evidence");
        var reader = Substitute.For<IResearchReader>();
        reader.ReadAsync(root, Arg.Any<Guid>(), ExecutionOrigin.UserTurn, Arg.Any<CancellationToken>())
            .Returns(Page(root, "Start", "Resources", new ResearchReference(asset, "Icon", ResearchReferenceKind.Api),
                new ResearchReference(evidence, "Evidence", ResearchReferenceKind.Page)));
        reader.ReadAsync(asset, Arg.Any<Guid>(), ExecutionOrigin.UserTurn, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ResearchPage>(new EgressRefusedException("Unsupported image")));
        reader.ReadAsync(evidence, Arg.Any<Guid>(), ExecutionOrigin.UserTurn, Arg.Any<CancellationToken>())
            .Returns(Page(evidence, "Evidence", "Useful material"));
        var service = new DeepResearchService(reader, Options.Create(new DeepResearchOptions { MaxPages = 2 }),
            NullLogger<DeepResearchService>.Instance, new ResearchJournal(Substitute.For<IResearchRunStore>(), TimeProvider.System));

        var result = await service.ExploreAsync(root, "Evidence", Guid.NewGuid(), ExecutionOrigin.UserTurn, CancellationToken.None);

        Assert.Equal(new[] { root, evidence }, result.Findings.Select(finding => finding.Page.Url));
    }

    [Fact]
    public async Task Explore_Should_Follow_Relevant_Data_References_And_Preserve_Their_Path()
    {
        var (service, reader, root, api, report, data, privatePage) = Subject();

        var result = await service.ExploreAsync(
            root, "county building permit data", Guid.NewGuid(), ExecutionOrigin.UserTurn, CancellationToken.None);

        Assert.Equal([root, api, data, report], result.Findings.Select(finding => finding.Page.Url));
        Assert.Equal([root, api, data], result.Findings.Single(finding => finding.Page.Url == data).Path);
        Assert.True(result.PageLimitReached);
        await reader.DidNotReceive().ReadAsync(
            Arg.Is<Uri>(address => address == privatePage), Arg.Any<Guid>(), Arg.Any<ExecutionOrigin>(), Arg.Any<CancellationToken>());
        await reader.Received(1).ReadAsync(
            api, Arg.Any<Guid>(), ExecutionOrigin.UserTurn, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("http")]
    public async Task Explore_Should_Continue_Siblings_After_A_Source_Fails(string failure)
    {
        var (service, reader, root, api, report, _, _) = Subject();
        reader.ReadAsync(new Uri("https://public.example/about"), Arg.Any<Guid>(),
                ExecutionOrigin.UserTurn, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ResearchPage>(new EgressRefusedException("Unavailable fixture source")));
        reader.ReadAsync(api, Arg.Any<Guid>(), ExecutionOrigin.UserTurn, Arg.Any<CancellationToken>())
            .Returns(_ => failure == "timeout"
                ? Task.FromException<ResearchPage>(new TaskCanceledException("Source timed out", new TimeoutException()))
                : Task.FromResult(new ResearchPage(api, 503, "Unavailable", "Server error")));

        var result = await service.ExploreAsync(
            root, "county building permit data", Guid.NewGuid(), ExecutionOrigin.UserTurn, CancellationToken.None);

        Assert.Equal(new[] { root, report }, result.Findings.Select(finding => finding.Page.Url));
    }

    [Fact]
    public async Task Explore_Should_Deduplicate_Starting_Pages_Before_Spending_The_Shared_Budget()
    {
        var (service, _, root, _, report, _, _) = Subject();

        var result = await service.ExploreAsync(
            new[] { root, new Uri(root + "#section"), report }, "county building permit data",
            Guid.NewGuid(), ExecutionOrigin.UserTurn, CancellationToken.None);

        Assert.Equal((4, 4), (result.PagesAttempted, result.Findings.Select(f => f.Page.Url).Distinct().Count()));
    }

    private static (DeepResearchService Service, IResearchReader Reader, Uri Root, Uri Api, Uri Report, Uri Data, Uri Private) Subject()
    {
        var reader = Substitute.For<IResearchReader>();
        var root = new Uri("https://public.example/start");
        var api = new Uri("https://api.example.net/permits");
        var report = new Uri("https://public.example/permit-report");
        var data = new Uri("https://data.example.net/permits.csv");
        var privatePage = new Uri("http://127.0.0.1/permit-admin");
        reader.ReadAsync(root, Arg.Any<Guid>(), ExecutionOrigin.UserTurn, Arg.Any<CancellationToken>())
            .Returns(Page(root, "Start", "County resources",
                new ResearchReference(new Uri("https://public.example/about"), "Permit office", ResearchReferenceKind.Page),
                new ResearchReference(api, "County permit API", ResearchReferenceKind.Api),
                new ResearchReference(report, "Building permit report", ResearchReferenceKind.Page),
                new ResearchReference(api, "Duplicate API", ResearchReferenceKind.Api),
                new ResearchReference(privatePage, "Private permit admin", ResearchReferenceKind.Api)));
        reader.ReadAsync(api, Arg.Any<Guid>(), ExecutionOrigin.UserTurn, Arg.Any<CancellationToken>())
            .Returns(Page(api, "Permit API", "Machine-readable permits",
                new ResearchReference(data, "permit data download", ResearchReferenceKind.Data)));
        reader.ReadAsync(report, Arg.Any<Guid>(), ExecutionOrigin.UserTurn, Arg.Any<CancellationToken>())
            .Returns(Page(report, "Permit report", "Permit totals by year"));
        reader.ReadAsync(data, Arg.Any<Guid>(), ExecutionOrigin.UserTurn, Arg.Any<CancellationToken>())
            .Returns(Page(data, "permits.csv", "year,total 2026,417"));
        var service = new DeepResearchService(
            reader,
            Options.Create(new DeepResearchOptions { MaxPages = 4, MaxDepth = 2, MaxReferencesPerPage = 8 }),
            NullLogger<DeepResearchService>.Instance, new ResearchJournal(Substitute.For<IResearchRunStore>(), TimeProvider.System));
        return (service, reader, root, api, report, data, privatePage);
    }

    private static ResearchPage Page(Uri url, string title, string text, params ResearchReference[] references) =>
        new(url, 200, title, text) { References = references };

    [Fact]
    public async Task Explore_Should_Keep_A_Hard_Ceiling_When_Configuration_Is_Excessive()
    {
        var reader = Substitute.For<IResearchReader>();
        var sequence = 0;
        reader.ReadAsync(Arg.Any<Uri>(), Arg.Any<Guid>(), Arg.Any<ExecutionOrigin>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var current = call.ArgAt<Uri>(0);
                var first = new Uri($"https://data.example.net/page-{++sequence}.json");
                var second = new Uri($"https://data.example.net/page-{++sequence}.json");
                return Page(current, "Data", "content",
                    new ResearchReference(first, "more data", ResearchReferenceKind.Data),
                    new ResearchReference(second, "more data", ResearchReferenceKind.Data));
            });
        var service = new DeepResearchService(reader,
            Options.Create(new DeepResearchOptions { MaxPages = 100, MaxDepth = 100, MaxReferencesPerPage = 100 }),
            NullLogger<DeepResearchService>.Instance, new ResearchJournal(Substitute.For<IResearchRunStore>(), TimeProvider.System));

        var result = await service.ExploreAsync(
            new Uri("https://public.example/start"), "data", Guid.NewGuid(), ExecutionOrigin.UserTurn, CancellationToken.None);

        Assert.Equal(HARD_PAGE_LIMIT, result.PagesAttempted);
        Assert.True(result.PageLimitReached);
    }
}
