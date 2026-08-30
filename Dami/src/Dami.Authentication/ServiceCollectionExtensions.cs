using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;
using static OpenIddict.Server.OpenIddictServerEvents;
using System.Security.Cryptography.X509Certificates;

namespace Dami.Authentication;

/// <summary>Composes the maintained OIDC authority and its isolated PostgreSQL stores.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Adds the enabled local authority.</summary>
    public static IServiceCollection AddDamiAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        DamiAuthenticationOptions options = ReadOptions(configuration, environment);
        services.Configure<DamiAuthenticationOptions>(
            configuration.GetSection(DamiAuthenticationOptions.SECTION_NAME));
        AddPersistence(services, connectionString);
        AddAuthority(services, options);
        services.AddScoped<DamiClientProvisioner>();
        services.AddScoped<DamiIdentityProvisioner>();

        // Nothing can authenticate until dami-cli and dami-gui exist as registrations.
        // They never did outside a test fixture (G5a).
        services.AddHostedService<FirstPartyClientSeeder>();

        // And nothing can log in until a human account exists. That had the same hole:
        // the endpoints check passwords against a user table nothing ever wrote to.
        services.AddHostedService<BootstrapIdentitySeeder>();
        return services;
    }

    private static void AddPersistence(IServiceCollection services, string connectionString)
    {
        services.AddDbContext<DamiAuthDbContext>(options =>
        {
            options.UseNpgsql(
                connectionString,
                postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "dami_auth"));
            options.UseOpenIddict();
        });
        services.AddIdentityCore<DamiIdentity>()
            .AddRoles<Microsoft.AspNetCore.Identity.IdentityRole<Guid>>()
            .AddEntityFrameworkStores<DamiAuthDbContext>();
    }

    private static void AddAuthority(
        IServiceCollection services,
        DamiAuthenticationOptions configuration)
    {
        services.AddOpenIddict()
            .AddCore(options => options.UseEntityFrameworkCore()
                .UseDbContext<DamiAuthDbContext>())
            .AddServer(options => ConfigureServer(options, configuration))
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.UseAspNetCore();
            });
        services.AddAuthentication(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireAssertion(HasRuntimeScope)
                .Build();
            options.AddPolicy(
                DamiAuthorizationPolicies.RUNTIME_READ,
                policy => policy.RequireAuthenticatedUser().RequireAssertion(
                    context => context.User.HasScope(DamiAuthorizationScopes.RUNTIME_READ)));
            options.AddPolicy(
                DamiAuthorizationPolicies.RUNTIME_WRITE,
                policy => policy.RequireAuthenticatedUser().RequireAssertion(
                    context => context.User.HasScope(DamiAuthorizationScopes.RUNTIME_WRITE)));
            options.AddPolicy(
                DamiAuthorizationPolicies.APPROVALS_RESOLVE,
                policy => policy.RequireAuthenticatedUser().RequireAssertion(
                    context => context.User.HasScope(DamiAuthorizationScopes.RUNTIME_WRITE)
                        && context.User.HasScope(DamiAuthorizationScopes.APPROVALS_RESOLVE)));
        });
    }

    private static bool HasRuntimeScope(AuthorizationHandlerContext context)
    {
        if (context.Resource is not HttpContext request)
        {
            return false;
        }

        var scope = HttpMethods.IsGet(request.Request.Method)
            || HttpMethods.IsHead(request.Request.Method)
                ? DamiAuthorizationScopes.RUNTIME_READ
                : DamiAuthorizationScopes.RUNTIME_WRITE;
        return context.User.HasScope(scope);
    }

    private static void ConfigureServer(
        OpenIddictServerBuilder server,
        DamiAuthenticationOptions configuration)
    {
        server.SetIssuer(new Uri(configuration.Issuer, UriKind.Absolute));
        server.SetAuthorizationEndpointUris("/connect/authorize");
        server.SetTokenEndpointUris("/connect/token");
        server.SetDeviceAuthorizationEndpointUris("/connect/device");
        server.SetEndUserVerificationEndpointUris("/connect/verify");
        server.AddEventHandler<HandleTokenRequestContext>(builder =>
            builder.UseScopedHandler<ClientCredentialsTokenHandler>());
        ConfigureFlows(server);
        server.RequireProofKeyForCodeExchange();
        ConfigureKeys(server, configuration);
        OpenIddictServerAspNetCoreBuilder aspNetCore = server.UseAspNetCore();
        aspNetCore.EnableAuthorizationEndpointPassthrough();
        aspNetCore.EnableEndUserVerificationEndpointPassthrough();
        if (configuration.AllowInsecureLoopback)
        {
            aspNetCore.DisableTransportSecurityRequirement();
        }
    }

    private static void ConfigureFlows(OpenIddictServerBuilder server)
    {
        server.RegisterScopes(
            DamiAuthorizationScopes.RUNTIME_READ,
            DamiAuthorizationScopes.RUNTIME_WRITE,
            DamiAuthorizationScopes.APPROVALS_RESOLVE);
        server.AllowAuthorizationCodeFlow();
        server.AllowClientCredentialsFlow();
        server.AllowDeviceAuthorizationFlow();
        server.AllowRefreshTokenFlow();
    }

    private static void ConfigureKeys(
        OpenIddictServerBuilder server,
        DamiAuthenticationOptions configuration)
    {
        if (configuration.UseEphemeralKeys)
        {
            server.AddEphemeralEncryptionKey();
            server.AddEphemeralSigningKey();
        }
        else
        {
            server.AddSigningCertificate(LoadCertificate(
                configuration.SigningCertificatePath!,
                configuration.SigningCertificatePassword!));
            server.AddEncryptionCertificate(LoadCertificate(
                configuration.EncryptionCertificatePath!,
                configuration.EncryptionCertificatePassword!));
        }
    }

    private static DamiAuthenticationOptions ReadOptions(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        var options = new DamiAuthenticationOptions();
        configuration.GetSection(DamiAuthenticationOptions.SECTION_NAME).Bind(options);
        if (!options.Enabled)
        {
            throw new InvalidOperationException("Dami authentication must be enabled when composed.");
        }

        ValidateIssuer(options);
        if (options.UseEphemeralKeys && !environment.IsEnvironment("Testing"))
        {
            throw new InvalidOperationException(
                "Ephemeral OIDC keys are restricted to the isolated Testing environment.");
        }

        if (!options.UseEphemeralKeys && !HasPersistentCertificates(options))
        {
            throw new InvalidOperationException(
                "Persistent OIDC signing and encryption certificates are required.");
        }

        return options;
    }

    private static void ValidateIssuer(DamiAuthenticationOptions options)
    {
        if (!Uri.TryCreate(options.Issuer, UriKind.Absolute, out Uri? issuer)
            || !string.IsNullOrEmpty(issuer.UserInfo)
            || !string.IsNullOrEmpty(issuer.Query)
            || !string.IsNullOrEmpty(issuer.Fragment)
            || (issuer.Scheme != Uri.UriSchemeHttps
                && !(options.AllowInsecureLoopback
                    && issuer.Scheme == Uri.UriSchemeHttp
                    && issuer.IsLoopback)))
        {
            throw new InvalidOperationException(
                "The OIDC issuer must use HTTPS; only an explicitly enabled loopback HTTP issuer is allowed.");
        }
    }

    private static bool HasPersistentCertificates(DamiAuthenticationOptions options)
    {
        return !string.IsNullOrWhiteSpace(options.SigningCertificatePath)
            && options.SigningCertificatePassword is not null
            && !string.IsNullOrWhiteSpace(options.EncryptionCertificatePath)
            && options.EncryptionCertificatePassword is not null;
    }

    private static X509Certificate2 LoadCertificate(string path, string password)
    {
        ValidatePrivateKeyFile(path);
        return X509CertificateLoader.LoadPkcs12FromFile(
            path, password, X509KeyStorageFlags.EphemeralKeySet);
    }

    private static void ValidatePrivateKeyFile(string path)
    {
        if (!Path.IsPathFullyQualified(path))
        {
            throw new InvalidOperationException("OIDC private-key paths must be absolute.");
        }

        const UnixFileMode exposed = UnixFileMode.GroupRead
            | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
            | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
        if (OperatingSystem.IsLinux() && (File.GetUnixFileMode(path) & exposed) != 0)
        {
            throw new InvalidOperationException(
                "OIDC private-key files cannot grant permissions to group or other users.");
        }
    }
}
