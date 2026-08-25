using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
        services.AddAuthentication();
        services.AddAuthorization();
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
        server.AllowAuthorizationCodeFlow();
        server.AllowDeviceAuthorizationFlow();
        server.AllowRefreshTokenFlow();
        server.RequireProofKeyForCodeExchange();
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

        OpenIddictServerAspNetCoreBuilder aspNetCore =
            server.UseAspNetCore();
        if (configuration.AllowInsecureLoopback)
        {
            aspNetCore.DisableTransportSecurityRequirement();
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
