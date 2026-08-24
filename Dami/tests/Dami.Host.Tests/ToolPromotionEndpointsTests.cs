using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dami.Capabilities.Sandboxed;
using Dami.Contracts.Approvals;
using Dami.Contracts.Capabilities;
using Dami.Contracts.Events;
using Dami.Contracts.ToolStaging;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Dami.Host.Tests;

public sealed class ToolPromotionEndpointsTests
{
    private static readonly DateTimeOffset at = DateTimeOffset.UnixEpoch;

    [Fact]
    public async Task Verify_Should_Return_Exact_Durable_Evidence_Async()
    {
        StagedToolProposal proposal = CreateProposal();
        var workflow = new StubWorkflow(proposal);
        await using WebApplicationFactory<Program> factory = CreateFactory(workflow);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/tool-proposals/{proposal.Request.ProposalId:D}/verify",
            new { artifactVersion = proposal.ArtifactVersion }, CancellationToken.None);
        ToolVerificationRecord evidence = (await response.Content
            .ReadFromJsonAsync<ToolVerificationRecord>(CancellationToken.None))!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((proposal.Request.ProposalId, proposal.ArtifactVersion),
            (evidence.ProposalId, evidence.ArtifactVersion));
    }

    [Fact]
    public async Task Promote_Should_Return_The_Single_Resolution_Approval_Async()
    {
        StagedToolProposal proposal = CreateProposal();
        var workflow = new StubWorkflow(proposal);
        await using WebApplicationFactory<Program> factory = CreateFactory(workflow);
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/tool-proposals/{proposal.Request.ProposalId:D}/promote",
            new { artifactVersion = proposal.ArtifactVersion }, CancellationToken.None);
        using JsonDocument promotion = (await response.Content
            .ReadFromJsonAsync<JsonDocument>(CancellationToken.None))!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(proposal.Request.ProposalId,
            promotion.RootElement.GetProperty("proposalId").GetGuid());
        Assert.Equal(ApprovalStatus.Pending.ToString(), promotion.RootElement
            .GetProperty("approval").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Verify_Should_Return_NotFound_For_An_Unknown_Proposal_Async()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory(
            new FailingWorkflow(new KeyNotFoundException("missing proposal")));
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/tool-proposals/{Guid.NewGuid():D}/verify",
            new { artifactVersion = new string('a', 64) }, CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Promote_Should_Return_Conflict_Before_Exact_Verification_Async()
    {
        await using WebApplicationFactory<Program> factory = CreateFactory(
            new FailingWorkflow(new InvalidOperationException("verification required")));
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/tool-proposals/{Guid.NewGuid():D}/promote",
            new { artifactVersion = new string('a', 64) }, CancellationToken.None);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory(IToolPromotionWorkflow workflow)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton(workflow)));
    }

    private static StagedToolProposal CreateProposal()
    {
        using var parameters = JsonDocument.Parse("""{"type":"object"}""");
        var schema = new CapabilityToolSchema(
            Guid.NewGuid(), "promotable", "Promote exact bytes.", parameters.RootElement);
        var artifact = new ToolProposalArtifact(
            schema, ["promotion"],
            new Dictionary<string, string> { ["Tool.cs"] = "source" },
            new Dictionary<string, string> { ["ToolTests.cs"] = "tests" },
            "The exact tool has been reviewed.", [Guid.NewGuid()],
            ToolExecutionProfile.PureComputation);
        var request = new ToolProposalRequest(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            ExecutionOrigin.UserTurn, artifact);
        return new StagedToolProposal(request, artifact.Version, at);
    }

    private sealed class StubWorkflow(StagedToolProposal proposal) : IToolPromotionWorkflow
    {
        public Task<ToolVerificationRecord> VerifyAsync(
            Guid proposalId,
            string artifactVersion,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new ToolVerificationRecord(
                Guid.NewGuid(), proposalId, artifactVersion, new string('a', 64),
                "tests_passed=1", at));
        }

        public Task<ToolPromotionRequest> RequestPromotionAsync(
            Guid proposalId,
            string artifactVersion,
            CancellationToken cancellationToken)
        {
            var approval = new ApprovalRequest(
                Guid.NewGuid(), proposal.Request.TraceId, ToolPromotionRequest.REQUESTED_BY,
                "promote reviewed tool", ToolPromotionRequest.SCOPE,
                ToolPromotionRequest.Resource(proposalId, artifactVersion), at,
                origin: proposal.Request.Origin, parentSpanId: proposal.Request.SpanId);
            return Task.FromResult(new ToolPromotionRequest(
                Guid.NewGuid(), proposalId, artifactVersion, approval));
        }
    }

    private sealed class FailingWorkflow(Exception failure) : IToolPromotionWorkflow
    {
        public Task<ToolVerificationRecord> VerifyAsync(
            Guid proposalId,
            string artifactVersion,
            CancellationToken cancellationToken) =>
            Task.FromException<ToolVerificationRecord>(failure);

        public Task<ToolPromotionRequest> RequestPromotionAsync(
            Guid proposalId,
            string artifactVersion,
            CancellationToken cancellationToken) =>
            Task.FromException<ToolPromotionRequest>(failure);
    }
}
