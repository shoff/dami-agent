using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dami.Contracts.Capabilities;
using Dami.Contracts.Events;
using Dami.Contracts.ToolStaging;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dami.Host.Tests;

public sealed class ToolProposalEndpointsTests
{
    private static readonly DateTimeOffset at =
        new(2026, 8, 24, 19, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task List_Should_Return_Compact_Proposal_Metadata()
    {
        StagedToolProposal proposal = CreateProposal();
        await using WebApplicationFactory<Program> factory = CreateFactory(proposal);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage listResponse = await client.GetAsync(
            "/tool-proposals?limit=1", CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        using JsonDocument list = (await listResponse.Content
            .ReadFromJsonAsync<JsonDocument>(CancellationToken.None))!;

        Assert.Equal(proposal.Request.ProposalId,
            list.RootElement[0].GetProperty("proposalId").GetGuid());
    }

    [Fact]
    public async Task Inspect_Should_Return_The_Exact_Artifact()
    {
        StagedToolProposal proposal = CreateProposal();
        await using WebApplicationFactory<Program> factory = CreateFactory(proposal);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage inspectResponse = await client.GetAsync(
            $"/tool-proposals/{proposal.Request.ProposalId:D}", CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, inspectResponse.StatusCode);
        using JsonDocument inspection = (await inspectResponse.Content
            .ReadFromJsonAsync<JsonDocument>(CancellationToken.None))!;

        Assert.Equal(
            ("source", proposal.ArtifactVersion),
            (
                inspection.RootElement.GetProperty("request").GetProperty("artifact")
                    .GetProperty("sourceFiles").GetProperty("ReviewTool.cs").GetString(),
                inspection.RootElement.GetProperty("artifactVersion").GetString()));
    }

    [Fact]
    public async Task List_Should_Default_To_A_Bounded_Review_Page()
    {
        var store = new StubStore(CreateProposal());
        await using WebApplicationFactory<Program> factory = CreateFactory(store);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            "/tool-proposals", CancellationToken.None);

        Assert.Equal((HttpStatusCode.OK, 20), (response.StatusCode, store.LastLimit));
    }

    [Fact]
    public async Task List_Should_Reject_An_Oversized_Review_Page_At_The_Http_Boundary()
    {
        var store = new StubStore(CreateProposal());
        await using WebApplicationFactory<Program> factory = CreateFactory(store);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetAsync(
            "/tool-proposals?limit=101", CancellationToken.None);

        Assert.Equal((HttpStatusCode.BadRequest, 0), (response.StatusCode, store.LastLimit));
    }

    private static WebApplicationFactory<Program> CreateFactory(StagedToolProposal proposal)
    {
        return CreateFactory(new StubStore(proposal));
    }

    private static WebApplicationFactory<Program> CreateFactory(StubStore store)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<IToolProposalStore>(store)));
    }

    private static StagedToolProposal CreateProposal()
    {
        using var parameters = JsonDocument.Parse("""{"type":"object"}""");
        var schema = new CapabilityToolSchema(
            Guid.NewGuid(), "review-tool", "Review an artifact.", parameters.RootElement);
        var artifact = new ToolProposalArtifact(
            schema, ["review"],
            new Dictionary<string, string> { ["ReviewTool.cs"] = "source" },
            new Dictionary<string, string> { ["ReviewToolTests.cs"] = "tests" },
            "Repeated defects justify automation.", [Guid.NewGuid()],
            ToolExecutionProfile.ReadOnly);
        var request = new ToolProposalRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            ExecutionOrigin.UserTurn, artifact);
        return new StagedToolProposal(request, artifact.Version, at);
    }

    private sealed class StubStore(StagedToolProposal proposal) : IToolProposalStore
    {
        public int LastLimit { get; private set; }

        public Task<StagedToolProposal> StageAsync(
            StagedToolProposal value,
            CancellationToken cancellationToken) => Task.FromResult(value);

        public Task<StagedToolProposal?> FindAsync(
            Guid proposalId,
            CancellationToken cancellationToken) =>
            Task.FromResult<StagedToolProposal?>(
                proposal.Request.ProposalId == proposalId ? proposal : null);

        public Task<IReadOnlyList<ToolProposalSummary>> ListAsync(
            int limit,
            CancellationToken cancellationToken)
        {
            this.LastLimit = limit;
            return Task.FromResult<IReadOnlyList<ToolProposalSummary>>
            ([new ToolProposalSummary(
                proposal.Request.ProposalId,
                proposal.Request.Artifact.Schema.CapabilityId,
                proposal.Request.Artifact.Schema.Name,
                proposal.ArtifactVersion,
                proposal.Request.Artifact.ExecutionProfile,
                proposal.Request.Origin,
                proposal.ProposedAt)]);
        }
    }
}
