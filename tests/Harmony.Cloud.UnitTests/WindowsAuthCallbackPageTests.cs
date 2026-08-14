using Harmony.Cloud.Api.Domain;
using Xunit;

namespace Harmony.Cloud.UnitTests;

public sealed class WindowsAuthCallbackPageTests
{
    [Fact]
    public void Authorization_query_is_forwarded_untouched()
    {
        // The code and state belong to the desktop app. This endpoint exists to hand them onward, so
        // anything lost or re-encoded here is a sign-in that fails for reasons the app cannot explain.
        var redirect = WindowsAuthCallbackPage.BuildRedirect(
            "harmonymusic", "?code=abc123&state=xyz%2F789");

        Assert.Equal("harmonymusic://callback?code=abc123&state=xyz%2F789", redirect);
    }

    [Fact]
    public void Legacy_scheme_query_parameter_is_dropped_from_the_forwarded_query()
    {
        // The scheme moved to a path segment, but an older client still sends it as a query
        // parameter. It is this endpoint's own routing input either way, never part of the
        // OAuth exchange, so it must not be handed on to the app.
        var redirect = WindowsAuthCallbackPage.BuildRedirect(
            "harmonymusic-dev", "?scheme=harmonymusic-dev&code=abc123");

        Assert.Equal("harmonymusic-dev://callback?code=abc123", redirect);
    }

    [Fact]
    public void Debug_and_release_schemes_are_both_answered()
    {
        Assert.StartsWith("harmonymusic://", WindowsAuthCallbackPage.BuildRedirect("harmonymusic", null));
        Assert.StartsWith("harmonymusic-dev://",
            WindowsAuthCallbackPage.BuildRedirect("harmonymusic-dev", null));
    }

    [Fact]
    public void Missing_scheme_falls_back_to_the_release_scheme()
    {
        Assert.Equal("harmonymusic://callback", WindowsAuthCallbackPage.BuildRedirect(null, null));
        Assert.Equal("harmonymusic://callback", WindowsAuthCallbackPage.BuildRedirect("", "?"));
    }

    [Theory]
    [InlineData("javascript")]
    [InlineData("https")]
    [InlineData("harmonymusic-evil")]
    [InlineData("HarmonyMusic")]
    public void Unknown_schemes_are_refused(string scheme)
    {
        // Without the allowlist this endpoint would redirect anywhere a link told it to, with the
        // user's authorization code attached.
        Assert.Null(WindowsAuthCallbackPage.BuildRedirect(scheme, "?code=abc123"));
    }

    [Fact]
    public void Rendered_page_performs_the_handoff_and_says_the_tab_can_be_closed()
    {
        var page = WindowsAuthCallbackPage.Render("harmonymusic://callback?code=abc123");

        Assert.Contains("harmonymusic://callback?code=abc123", page);
        Assert.Contains("location.replace(", page);
        Assert.Contains("http-equiv=\"refresh\"", page);
        Assert.Contains("close this tab", page);
    }

    [Fact]
    public void Rendered_page_escapes_the_query_it_echoes()
    {
        // The query string is attacker-reachable: it arrives on a link anyone can craft, and it is
        // written into both an HTML attribute and a script literal.
        var page = WindowsAuthCallbackPage.Render(
            "harmonymusic://callback?state=</script><script>alert('x')</script>");

        Assert.DoesNotContain("<script>alert(", page);
        Assert.Contains("&lt;/script&gt;", page);
    }

    [Fact]
    public void Rendered_page_leaves_no_placeholder_unsubstituted()
    {
        // The markup lives in an embedded .html file, so its placeholders and the constants in
        // WindowsAuthCallbackPage.cs can drift apart without breaking the build. Renaming one in the
        // template would otherwise ship a page whose location.replace calls a literal
        // "{{REDIRECT_JS}}" — every other assertion here would still pass.
        var page = WindowsAuthCallbackPage.Render("harmonymusic://callback?code=abc123");

        Assert.DoesNotContain("{{", page, StringComparison.Ordinal);
        Assert.DoesNotContain("}}", page, StringComparison.Ordinal);
    }

    [Fact]
    public void Rendered_page_references_no_external_host()
    {
        // Self-contained by design: a page that fetches nothing cannot leak the query string onward.
        var page = WindowsAuthCallbackPage.Render("harmonymusic://callback?code=abc123");

        Assert.DoesNotContain("http://", page);
        Assert.DoesNotContain("https://", page);
    }
}
