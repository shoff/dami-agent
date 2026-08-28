using System.Net.Http.Headers;
using Xunit;

namespace Dami.Authentication.Tests;

public sealed class DamiBearerTokenTests
{
    [Fact]
    public void Apply_Should_Set_A_Nonempty_Bearer_Token()
    {
        using var client = new HttpClient();

        DamiBearerToken.Apply(client, "access-token");

        Assert.Equal(new AuthenticationHeaderValue("Bearer", "access-token"),
            client.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public void Apply_Should_Leave_Authentication_Unset_When_Token_Is_Missing()
    {
        using var client = new HttpClient();

        DamiBearerToken.Apply(client, null);

        Assert.Null(client.DefaultRequestHeaders.Authorization);
    }
}
