using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dami.Contracts.Domains;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Dami.Host.Tests;

public sealed class DomainEndpointsTests
{
    private static readonly DateTimeOffset at = new(2026, 8, 25, 22, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Domains_Should_List_Serve_A_Timeline_And_Reject_By_Prefix()
    {
        var fact = new DomainFact(Guid.NewGuid(), "network", new DateOnly(2026, 8, 25), "gateway", "Default gateway is 192.168.4.1", "network-collector", at);
        var store = new StubStore(fact);
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IDomainFactStore>();
                services.AddSingleton<IDomainFactStore>(store);
            }));
        using var client = factory.CreateClient();

        using var domains = await client.GetAsync("/domains", CancellationToken.None);
        using var listed = await domains.Content.ReadFromJsonAsync<JsonDocument>();
        using var timeline = await client.GetAsync("/domains/Network", CancellationToken.None);
        using var facts = await timeline.Content.ReadFromJsonAsync<JsonDocument>();
        using var rejected = await client.PostAsJsonAsync(
            $"/domains/facts/{fact.FactId.ToString("N")[..8]}/reject", new { reason = "wrong" }, CancellationToken.None);
        using var missing = await client.PostAsJsonAsync("/domains/facts/ffffffff/reject", new { reason = "x" }, CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, domains.StatusCode);
        Assert.Equal("network", listed!.RootElement[0].GetProperty("domain").GetString());
        Assert.Equal(fact.FactId, facts!.RootElement[0].GetProperty("factId").GetGuid());
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal((fact.FactId, "wrong"), (store.RejectedId, store.Reason));
    }

    private sealed class StubStore(DomainFact fact) : IDomainFactStore
    {
        internal Guid? RejectedId { get; private set; }

        internal string? Reason { get; private set; }

        public Task<bool> RecordAsync(DomainFact recorded, CancellationToken cancellationToken)
        {
            return Task.FromResult(true);
        }

        public async IAsyncEnumerable<DomainFact> TimelineAsync(
            string? domain, int limit, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            if (domain is null || domain == fact.Domain)
            {
                yield return fact;
            }
        }

        public IAsyncEnumerable<DomainFact> BetweenAsync(
            string domain, DateOnly from, DateOnly to, int limit, CancellationToken cancellationToken)
        {
            return this.TimelineAsync(domain, limit, cancellationToken);
        }

        public Task<bool> RejectAsync(Guid factId, string reason, CancellationToken cancellationToken)
        {
            this.RejectedId = factId;
            this.Reason = reason;
            return Task.FromResult(true);
        }

        public Task<IReadOnlyList<(string Domain, int Facts)>> DomainsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<(string, int)>>([(fact.Domain, 1)]);
        }
    }
}
