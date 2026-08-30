using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Authentication;

/// <summary>Creates the first-party client registrations at startup, once.</summary>
/// <remarks>
/// A hosted service rather than a line in the composition root, because provisioning needs
/// a scope and the database, and neither exists while services are still being registered.
///
/// It logs what it created and what was already there. The gap this closes was invisible
/// precisely because nothing said anything: the profiles existed, the endpoints answered,
/// the tests passed, and the registrations were never written outside a fixture.
/// </remarks>
public sealed class FirstPartyClientSeeder : BackgroundService
{
    private readonly IServiceScopeFactory scopes;
    private readonly IOptions<DamiAuthenticationOptions> options;
    private readonly ILogger<FirstPartyClientSeeder> logger;

    /// <summary>Creates the seeder.</summary>
    public FirstPartyClientSeeder(
        IServiceScopeFactory scopes,
        IOptions<DamiAuthenticationOptions> options,
        ILogger<FirstPartyClientSeeder> logger)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        this.scopes = scopes;
        this.options = options;
        this.logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Uri.TryCreate(this.options.Value.GuiRedirectUri, UriKind.Absolute, out var redirect))
        {
            this.logger.LogError(
                "Authentication:GuiRedirectUri is not an absolute URI ({Value}); no clients provisioned",
                this.options.Value.GuiRedirectUri);
            return;
        }

        using var scope = this.scopes.CreateScope();
        var provisioner = scope.ServiceProvider.GetRequiredService<DamiClientProvisioner>();
        var created = await provisioner
            .EnsureFirstPartyClientsAsync(redirect, stoppingToken)
            .ConfigureAwait(false);

        if (created.Count == 0)
        {
            this.logger.LogInformation("First-party clients already registered");
            return;
        }

        this.logger.LogInformation(
            "Registered first-party clients: {Created}", string.Join(", ", created));
    }
}
