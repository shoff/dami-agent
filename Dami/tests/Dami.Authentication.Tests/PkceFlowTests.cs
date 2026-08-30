using Xunit;

namespace Dami.Authentication.Tests;

public sealed class PkceFlowTests
{
    [Fact]
    public void Challenge_Should_Match_The_RFC_7636_Appendix_B_Vector()
    {
        Assert.Equal(
            "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
            PkceFlow.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));
    }

    [Fact]
    public void CreateVerifier_Should_Produce_43_Unreserved_Characters()
    {
        // 32 random bytes, base64url, no padding. RFC 7636 §4.1 requires 43–128 characters
        // from the unreserved set; anything outside it would need escaping on the wire.
        Assert.Matches("^[A-Za-z0-9_-]{43}$", PkceFlow.CreateVerifier());
    }

    [Fact]
    public void CreateVerifier_Should_Not_Repeat()
    {
        Assert.NotEqual(PkceFlow.CreateVerifier(), PkceFlow.CreateVerifier());
    }

    [Fact]
    public void ReadCallback_Should_Read_The_Code()
    {
        var callback = PkceFlow.ReadCallback(
            new Uri("http://127.0.0.1:5899/connect/callback?code=c-1&state=s-1"), "s-1");

        Assert.Equal("c-1", callback.Code);
    }

    [Fact]
    public void ReadCallback_Should_Refuse_A_State_Mismatch()
    {
        // Without the state check, any page that can make the client visit a URL can hand
        // it someone else's authorization code. A mismatch is an attack until proven a bug.
        var callback = PkceFlow.ReadCallback(
            new Uri("http://127.0.0.1:5899/connect/callback?code=c-1&state=someone-elses"), "s-1");

        Assert.Null(callback.Code);
    }

    [Fact]
    public void ReadCallback_Should_Refuse_A_Missing_State()
    {
        var callback = PkceFlow.ReadCallback(
            new Uri("http://127.0.0.1:5899/connect/callback?code=c-1"), "s-1");

        Assert.Null(callback.Code);
    }

    [Fact]
    public void ReadCallback_Should_Surface_The_Error_Parameter()
    {
        var callback = PkceFlow.ReadCallback(
            new Uri("http://127.0.0.1:5899/connect/callback?error=access_denied&state=s-1"), "s-1");

        Assert.Equal("access_denied", callback.Error);
    }

    [Fact]
    public void ReadCallback_Should_Refuse_A_Redirect_With_No_Code()
    {
        var callback = PkceFlow.ReadCallback(
            new Uri("http://127.0.0.1:5899/connect/callback?state=s-1"), "s-1");

        Assert.NotNull(callback.Error);
    }

    [Fact]
    public void ReadCallback_Should_Unescape_Query_Values()
    {
        var callback = PkceFlow.ReadCallback(
            new Uri("http://127.0.0.1:5899/connect/callback?code=c%2B1&state=s-1"), "s-1");

        Assert.Equal("c+1", callback.Code);
    }
}
