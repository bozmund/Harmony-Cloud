using System.Net;
using Xunit;

namespace Harmony.Cloud.IntegrationTests;

/// <summary>
/// The Windows sign-in landing page. Unlike every other route here, its caller is a browser
/// finishing an Auth0 redirect with no bearer token — so the one thing that must never regress is
/// that it is reachable without authentication. Mapped on the app rather than the <c>/cloud/v1</c>
/// group for exactly that reason; moving it under the group would answer 401 and break sign-in on
/// every Windows install.
/// </summary>
[Collection(CloudApiCollection.Name)]
public sealed class WindowsAuthCallbackEndpointTests(CloudApiFixture fixture)
{
    private const string Route = "/cloud/auth/windows/callback";

    [Theory]
    [InlineData("harmonymusic")]
    [InlineData("harmonymusic-dev")]
    public async Task Callback_page_is_served_without_a_bearer_token(string scheme)
    {
        // Deliberately a bare client: no X-Test-Subject header, no token.
        var client = fixture.Factory.CreateClient();

        var response = await client.GetAsync($"{Route}/{scheme}?code=abc123&state=xyz789");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        // The handoff itself, with the authorization code carried through untouched.
        Assert.Contains($"{scheme}://callback?code=abc123&amp;state=xyz789", body);
        Assert.Contains("close this tab", body);
    }

    [Fact]
    public async Task Callback_page_is_not_cached()
    {
        var client = fixture.Factory.CreateClient();

        var response = await client.GetAsync($"{Route}/harmonymusic?code=abc123");

        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task An_unknown_scheme_is_refused()
    {
        // Without this the endpoint would redirect anywhere a crafted link asked it to, carrying
        // the user's authorization code along.
        var client = fixture.Factory.CreateClient();

        var response = await client.GetAsync($"{Route}/evil-scheme?code=abc123");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_missing_scheme_segment_is_not_the_callback_route()
    {
        var client = fixture.Factory.CreateClient();

        var response = await client.GetAsync($"{Route}?code=abc123");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
