using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dami.Contracts.Privacy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Dami.Host.Tests;

public sealed class DisclosureEndpointsTests
{
    private static readonly DateTimeOffset at = new(2026, 8, 25, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Disclosures_Should_List_Recent_Decisions_And_Correct_One_By_Prefix()
    {
        var decisionId = Guid.NewGuid();
        var ledger = new StubLedger(new DisclosureDecision(
            decisionId, Guid.NewGuid(), "q", "Steve's surgeon is Dr Harrison", Disclosure.Pass,
            "Steve's surgeon is Dr Harrison", "public", at, null));
        await using var factory = CreateFactory(ledger);
        using var client = factory.CreateClient();

        using var list = await client.GetAsync("/disclosures", CancellationToken.None);
        using var listed = await list.Content.ReadFromJsonAsync<JsonDocument>();
        using var corrected = await client.PostAsJsonAsync(
            $"/disclosures/{decisionId.ToString("N")[..8]}/correct",
            new { disclosure = "withhold", note = "doctors' names never leave", correctedBy = "steve" },
            CancellationToken.None);
        using var missing = await client.PostAsJsonAsync(
            "/disclosures/ffffffff/correct", new { disclosure = "pass" }, CancellationToken.None);
        using var invalid = await client.PostAsJsonAsync(
            $"/disclosures/{decisionId.ToString("N")[..8]}/correct", new { disclosure = "maybe" }, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Equal(decisionId, listed!.RootElement[0].GetProperty("decisionId").GetGuid());
        Assert.Equal(HttpStatusCode.OK, corrected.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal((decisionId, Disclosure.Withhold, "doctors' names never leave", "steve"),
            (ledger.CorrectedId, ledger.Correction!.Corrected, ledger.Correction.Note, ledger.Correction.CorrectedBy));
    }

    private static WebApplicationFactory<Program> CreateFactory(IDisclosureLedger ledger)
    {
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDisclosureLedger>();
                services.AddSingleton(ledger);
            }));
    }

    private sealed class StubLedger(DisclosureDecision decision) : IDisclosureLedger
    {
        internal Guid? CorrectedId { get; private set; }

        internal DisclosureCorrection? Correction { get; private set; }

        public Task RecordAsync(
            Guid traceId, string question, IReadOnlyList<DisclosedItem> decisions, DateTimeOffset decidedAt,
            CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DisclosureDecision>> RecentAsync(int limit, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<DisclosureDecision>>([decision]);
        }

        public Task<bool> CorrectAsync(Guid decisionId, DisclosureCorrection correction, CancellationToken cancellationToken)
        {
            this.CorrectedId = decisionId;
            this.Correction = correction;
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<DisclosureDecision>> CorrectionsAsync(int limit, CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<DisclosureDecision>>([]);
        }
    }
}
