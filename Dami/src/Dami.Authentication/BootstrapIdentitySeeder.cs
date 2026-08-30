using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Dami.Authentication;

/// <summary>Creates the configured bootstrap account at startup, once.</summary>
/// <remarks>
/// Configuration-driven so the password never touches the repository: the username rides
/// ordinary configuration, the password arrives through secret configuration
/// (<c>Authentication__BootstrapPassword</c> — two underscores). With neither set the
/// seeder stays silent-but-logged, which is the normal state once the account exists.
/// </remarks>
public sealed class BootstrapIdentitySeeder : BackgroundService
{
    private readonly IServiceScopeFactory scopes;
    private readonly IOptions<DamiAuthenticationOptions> options;
    private readonly ILogger<BootstrapIdentitySeeder> logger;

    /// <summary>Creates the seeder.</summary>
    public BootstrapIdentitySeeder(
        IServiceScopeFactory scopes,
        IOptions<DamiAuthenticationOptions> options,
        ILogger<BootstrapIdentitySeeder> logger)
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
        var username = this.options.Value.BootstrapUsername;
        var password = this.options.Value.BootstrapPassword;
        if (string.IsNullOrWhiteSpace(username) && string.IsNullOrEmpty(password))
        {
            this.logger.LogInformation("No bootstrap identity configured; none created");
            return;
        }

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            this.logger.LogError(
                "Bootstrap identity needs both Authentication:BootstrapUsername and "
                + "Authentication:BootstrapPassword; only one is set, none created");
            return;
        }

        using var scope = this.scopes.CreateScope();
        var provisioner = scope.ServiceProvider.GetRequiredService<DamiIdentityProvisioner>();
        var outcome = await provisioner
            .EnsureIdentityAsync(username, password, stoppingToken)
            .ConfigureAwait(false);
        this.Report(username, outcome);
    }

    private void Report(string username, IdentityProvision outcome)
    {
        switch (outcome.Result)
        {
            case IdentityProvisionResult.Created:
                this.logger.LogInformation("Created bootstrap identity {Username}", username);
                break;
            case IdentityProvisionResult.AlreadyExists:
                this.logger.LogInformation(
                    "Bootstrap identity {Username} already exists; left untouched", username);
                break;
            case IdentityProvisionResult.Failed:
            default:
                this.logger.LogError(
                    "Bootstrap identity {Username} was refused: {Error}", username, outcome.Error);
                break;
        }
    }
}
