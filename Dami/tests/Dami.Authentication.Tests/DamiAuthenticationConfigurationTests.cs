using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Claims;
using System.Runtime.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using OpenIddict.Abstractions;
using Xunit;

namespace Dami.Authentication.Tests;

[SupportedOSPlatform("linux")]
public sealed class DamiAuthenticationConfigurationTests
{
    [Fact]
    public async Task Fallback_Policy_Should_Separate_Runtime_Reads_From_Writes()
    {
        using var certificates = new CertificateFiles();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDamiAuthentication(
            Configuration(certificates.Signing, certificates.Encryption, certificates.Password),
            new TestEnvironment("Production"),
            "Host=127.0.0.1;Database=unused;Username=unused;Password=unused");
        await using ServiceProvider provider = services.BuildServiceProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        AuthorizationPolicy fallback = (await provider.GetRequiredService<IAuthorizationPolicyProvider>()
            .GetFallbackPolicyAsync())!;

        AuthorizationResult read = await authorization.AuthorizeAsync(
            Principal(DamiAuthorizationScopes.RUNTIME_READ), Request(HttpMethods.Get), fallback);
        AuthorizationResult readWriting = await authorization.AuthorizeAsync(
            Principal(DamiAuthorizationScopes.RUNTIME_READ), Request(HttpMethods.Post), fallback);
        AuthorizationResult write = await authorization.AuthorizeAsync(
            Principal(DamiAuthorizationScopes.RUNTIME_WRITE), Request(HttpMethods.Post), fallback);

        Assert.True(read.Succeeded);
        Assert.False(readWriting.Succeeded);
        Assert.True(write.Succeeded);
    }

    [Fact]
    public async Task Runtime_Policies_Should_Require_Their_Matching_Scopes()
    {
        using var certificates = new CertificateFiles();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDamiAuthentication(
            Configuration(certificates.Signing, certificates.Encryption, certificates.Password),
            new TestEnvironment("Production"),
            "Host=127.0.0.1;Database=unused;Username=unused;Password=unused");
        await using ServiceProvider provider = services.BuildServiceProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        ClaimsPrincipal reader = Principal(DamiAuthorizationScopes.RUNTIME_READ);
        ClaimsPrincipal writer = Principal(DamiAuthorizationScopes.RUNTIME_WRITE);

        Assert.True((await authorization.AuthorizeAsync(
            reader, null, DamiAuthorizationPolicies.RUNTIME_READ)).Succeeded);
        Assert.False((await authorization.AuthorizeAsync(
            reader, null, DamiAuthorizationPolicies.RUNTIME_WRITE)).Succeeded);
        Assert.True((await authorization.AuthorizeAsync(
            writer, null, DamiAuthorizationPolicies.RUNTIME_WRITE)).Succeeded);
    }

    [Fact]
    public async Task Approval_Resolution_Policy_Should_Require_Its_Dedicated_Scope()
    {
        using var certificates = new CertificateFiles();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDamiAuthentication(
            Configuration(certificates.Signing, certificates.Encryption, certificates.Password),
            new TestEnvironment("Production"),
            "Host=127.0.0.1;Database=unused;Username=unused;Password=unused");
        await using ServiceProvider provider = services.BuildServiceProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        ClaimsPrincipal approvalOnly = Principal(DamiAuthorizationScopes.APPROVALS_RESOLVE);
        ClaimsPrincipal withBoth = Principal(
            DamiAuthorizationScopes.RUNTIME_WRITE,
            DamiAuthorizationScopes.APPROVALS_RESOLVE);

        AuthorizationResult denied = await authorization.AuthorizeAsync(
            approvalOnly, null, DamiAuthorizationPolicies.APPROVALS_RESOLVE);
        AuthorizationResult allowed = await authorization.AuthorizeAsync(
            withBoth, null, DamiAuthorizationPolicies.APPROVALS_RESOLVE);

        Assert.False(denied.Succeeded);
        Assert.True(allowed.Succeeded);
    }

    [Fact]
    public void Configuration_Should_Reject_Insecure_NonLoopback_Issuer()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
        [
            new KeyValuePair<string, string?>("Authentication:Enabled", "true"),
            new KeyValuePair<string, string?>("Authentication:Issuer", "http://192.0.2.1:5810/"),
            new KeyValuePair<string, string?>("Authentication:AllowInsecureLoopback", "true"),
            new KeyValuePair<string, string?>("Authentication:UseEphemeralKeys", "true"),
        ]).Build();

        Assert.Throws<InvalidOperationException>(() => new ServiceCollection()
            .AddDamiAuthentication(
                configuration,
                new TestEnvironment("Testing"),
                "Host=127.0.0.1;Database=unused;Username=unused;Password=unused"));
    }

    [Fact]
    public void Production_Should_Reject_Ephemeral_Keys()
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
        [
            new KeyValuePair<string, string?>("Authentication:Enabled", "true"),
            new KeyValuePair<string, string?>("Authentication:Issuer", "https://localhost:5810/"),
            new KeyValuePair<string, string?>("Authentication:UseEphemeralKeys", "true"),
        ]).Build();
        var services = new ServiceCollection();

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() =>
            services.AddDamiAuthentication(
                configuration,
                new TestEnvironment("Production"),
                "Host=127.0.0.1;Database=unused;Username=unused;Password=unused"));

        Assert.Contains("isolated Testing environment", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_Should_Load_External_Signing_And_Encryption_Certificates()
    {
        using var certificates = new CertificateFiles();
        IConfiguration configuration = Configuration(
            certificates.Signing, certificates.Encryption, certificates.Password);
        var services = new ServiceCollection();

        services.AddDamiAuthentication(
            configuration,
            new TestEnvironment("Production"),
            "Host=127.0.0.1;Database=unused;Username=unused;Password=unused");

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType.FullName == "OpenIddict.Server.IOpenIddictServerDispatcher");
    }

    [Fact]
    public void Production_Should_Reject_Group_Readable_Private_Key_Files()
    {
        using var certificates = new CertificateFiles();
        certificates.MakeSigningGroupReadable();
        IConfiguration configuration = Configuration(
            certificates.Signing, certificates.Encryption, certificates.Password);

        Assert.Throws<InvalidOperationException>(() => new ServiceCollection()
            .AddDamiAuthentication(
                configuration,
                new TestEnvironment("Production"),
                "Host=127.0.0.1;Database=unused;Username=unused;Password=unused"));
    }

    private static IConfiguration Configuration(
        string signing,
        string encryption,
        string password)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(
        [
            new KeyValuePair<string, string?>("Authentication:Enabled", "true"),
            new KeyValuePair<string, string?>("Authentication:Issuer", "https://localhost:5810/"),
            new KeyValuePair<string, string?>("Authentication:SigningCertificatePath", signing),
            new KeyValuePair<string, string?>("Authentication:SigningCertificatePassword", password),
            new KeyValuePair<string, string?>("Authentication:EncryptionCertificatePath", encryption),
            new KeyValuePair<string, string?>("Authentication:EncryptionCertificatePassword", password),
        ]).Build();
    }

    private static ClaimsPrincipal Principal(params string[] scopes)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity([], "test"));
        principal.SetScopes(scopes);
        return principal;
    }

    private static HttpContext Request(string method)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        return context;
    }

    private static void WriteCertificate(string path, string subject, string password)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using X509Certificate2 certificate = request.CreateSelfSigned(
            new DateTimeOffset(2026, 8, 24, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2036, 8, 24, 0, 0, 0, TimeSpan.Zero));
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, password));
    }

    private sealed class TestEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "Dami.Authentication.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class CertificateFiles : IDisposable
    {
        private readonly string directory = Path.Combine(
            Path.GetTempPath(), $"dami-auth-certificates-{Guid.NewGuid():N}");

        public CertificateFiles()
        {
            Directory.CreateDirectory(this.directory);
            this.Signing = Path.Combine(this.directory, "signing.pfx");
            this.Encryption = Path.Combine(this.directory, "encryption.pfx");
            WriteCertificate(this.Signing, "CN=Dami Test Signing", this.Password);
            WriteCertificate(this.Encryption, "CN=Dami Test Encryption", this.Password);
            File.SetUnixFileMode(this.Signing, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.SetUnixFileMode(this.Encryption, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        public string Encryption { get; }

        public string Password { get; } = "test-only-password";

        public string Signing { get; }

        public void MakeSigningGroupReadable()
        {
            File.SetUnixFileMode(
                this.Signing,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        }

        public void Dispose()
        {
            Directory.Delete(this.directory, recursive: true);
        }
    }
}
