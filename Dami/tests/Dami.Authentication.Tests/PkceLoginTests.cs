using System.Net;
using Xunit;

namespace Dami.Authentication.Tests;

public sealed class PkceLoginTests
{
    private static readonly Uri host = new("http://127.0.0.1:5810/");
    private static readonly Uri redirect = new("http://127.0.0.1:5899/connect/callback");

    [Fact]
    public async Task LogInAsync_Should_Return_The_Granted_Token()
    {
        var authority = new ScriptedAuthority();
        var login = new PkceLogin(new HttpClient(authority));

        var poll = await login.LogInAsync(host, redirect, "steve", "pw", CancellationToken.None);

        Assert.Equal("at-1", poll.Token?.AccessToken);
    }

    [Fact]
    public async Task LogInAsync_Should_Prove_The_Challenge_With_Its_Verifier()
    {
        // The whole point of PKCE: the verifier presented at the token endpoint must be
        // the preimage of the challenge sent with the authorization request.
        var authority = new ScriptedAuthority();
        var login = new PkceLogin(new HttpClient(authority));

        await login.LogInAsync(host, redirect, "steve", "pw", CancellationToken.None);

        Assert.Equal(authority.SentChallenge, PkceFlow.Challenge(authority.SentVerifier!));
    }

    [Fact]
    public async Task LogInAsync_Should_Treat_A_401_As_Denied()
    {
        var authority = new ScriptedAuthority { AuthorizeStatus = HttpStatusCode.Unauthorized };
        var login = new PkceLogin(new HttpClient(authority));

        var poll = await login.LogInAsync(host, redirect, "steve", "wrong", CancellationToken.None);

        Assert.Equal(DevicePollResult.Denied, poll.Result);
    }

    [Fact]
    public async Task LogInAsync_Should_Fail_On_A_Response_That_Is_Not_A_Redirect()
    {
        var authority = new ScriptedAuthority { AuthorizeStatus = HttpStatusCode.OK };
        var login = new PkceLogin(new HttpClient(authority));

        var poll = await login.LogInAsync(host, redirect, "steve", "pw", CancellationToken.None);

        Assert.Equal(DevicePollResult.Failed, poll.Result);
    }

    [Fact]
    public async Task LogInAsync_Should_Fail_On_A_Redirect_With_The_Wrong_State()
    {
        var authority = new ScriptedAuthority { EchoState = "someone-elses" };
        var login = new PkceLogin(new HttpClient(authority));

        var poll = await login.LogInAsync(host, redirect, "steve", "pw", CancellationToken.None);

        Assert.Equal(DevicePollResult.Failed, poll.Result);
    }

    [Fact]
    public async Task LogInAsync_Should_Not_Exchange_A_Code_That_Arrived_With_The_Wrong_State()
    {
        // A poisoned redirect must end the flow, not merely be reported: exchanging the
        // code anyway would complete a login the user never started.
        var authority = new ScriptedAuthority { EchoState = "someone-elses" };
        var login = new PkceLogin(new HttpClient(authority));

        await login.LogInAsync(host, redirect, "steve", "pw", CancellationToken.None);

        Assert.Null(authority.SentVerifier);
    }

    /// <summary>Plays the host's two endpoints, recording what the client sent.</summary>
    private sealed class ScriptedAuthority : HttpMessageHandler
    {
        public string? SentChallenge { get; private set; }

        public string? SentVerifier { get; private set; }

        /// <summary>State echoed on the redirect; null echoes what the client sent.</summary>
        public string? EchoState { get; set; }

        public HttpStatusCode AuthorizeStatus { get; set; } = HttpStatusCode.Found;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var form = ParseForm(await request.Content!.ReadAsStringAsync(cancellationToken));
            return request.RequestUri!.AbsolutePath == "/connect/authorize"
                ? this.Authorize(form)
                : this.Exchange(form);
        }

        private HttpResponseMessage Authorize(Dictionary<string, string> form)
        {
            if (this.AuthorizeStatus != HttpStatusCode.Found)
            {
                return new HttpResponseMessage(this.AuthorizeStatus);
            }

            this.SentChallenge = form["code_challenge"];
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri(
                form["redirect_uri"] + "?code=c-1&state="
                + Uri.EscapeDataString(this.EchoState ?? form["state"]));
            return response;
        }

        private HttpResponseMessage Exchange(Dictionary<string, string> form)
        {
            this.SentVerifier = form["code_verifier"];
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"access_token":"at-1","expires_in":3600}"""),
            };
        }

        private static Dictionary<string, string> ParseForm(string body)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pair in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var split = pair.Split('=', 2);
                values[Uri.UnescapeDataString(split[0])] =
                    split.Length == 2 ? Uri.UnescapeDataString(split[1]) : string.Empty;
            }

            return values;
        }
    }
}
