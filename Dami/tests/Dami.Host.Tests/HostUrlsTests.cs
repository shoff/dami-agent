using Microsoft.Extensions.Configuration;
using Xunit;

namespace Dami.Host.Tests;

/// <summary>D-005 in code: the API leaves loopback only behind authentication (ADR-0020).</summary>
public sealed class HostUrlsTests
{
    [Fact]
    public void Resolve_Should_Default_To_Loopback()
    {
        Assert.Equal("http://127.0.0.1:5810", HostUrls.Resolve(Configure()));
    }

    [Fact]
    public void Resolve_Should_Honour_A_Loopback_Override()
    {
        Assert.Equal("http://127.0.0.1:5811", HostUrls.Resolve(Configure(("Host:Urls", "http://127.0.0.1:5811"))));
    }

    [Fact]
    public void Resolve_Should_Refuse_A_Lan_Address_Without_Authentication()
    {
        var configuration = Configure(("Host:Urls", "http://127.0.0.1:5810;http://192.168.4.45:5810"));

        Assert.Throws<InvalidOperationException>(() => HostUrls.Resolve(configuration));
    }

    [Fact]
    public void Resolve_Should_Refuse_Any_Address_Without_Authentication()
    {
        var configuration = Configure(("Host:Urls", "http://0.0.0.0:5810"));

        Assert.Throws<InvalidOperationException>(() => HostUrls.Resolve(configuration));
    }

    [Fact]
    public void Resolve_Should_Allow_A_Lan_Address_With_Authentication_On()
    {
        var configuration = Configure(
            ("Host:Urls", "http://127.0.0.1:5810;http://192.168.4.45:5810"), ("Authentication:Enabled", "true"));

        Assert.Equal("http://127.0.0.1:5810;http://192.168.4.45:5810", HostUrls.Resolve(configuration));
    }

    [Fact]
    public void Resolve_Should_Refuse_A_Malformed_Url()
    {
        Assert.Throws<InvalidOperationException>(() => HostUrls.Resolve(Configure(("Host:Urls", "not a url"))));
    }

    private static IConfiguration Configure(params (string Key, string Value)[] settings)
    {
        var values = new Dictionary<string, string?>();
        foreach (var (key, value) in settings)
        {
            values[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
}
