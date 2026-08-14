using System.Net;
using System.Text;

namespace Harmony.Cloud.Api.Domain;

/// <summary>
/// The page the Harmony Music Windows app's sign-in lands on before handing back to the desktop app.
/// </summary>
/// <remarks>
/// <para>
/// Windows auth is a custom-scheme flow: the browser ends up at <c>harmonymusic://callback?code=…</c>,
/// which the OS routes to the installed app. Sending Auth0 straight there works, but leaves the
/// browser tab parked on the last page it committed — the Auth0 login form or the Google account
/// chooser — as if the sign-in had stalled.
/// </para>
/// <para>
/// The sign-OUT tab does close itself, which makes the login tab look broken by comparison. That is
/// incidental browser behaviour, not something the app does: the logout URL redirects to the custom
/// scheme without ever committing a page, and a tab with empty history is one the browser will close
/// on its own once it hands off to an external protocol handler. A login tab always commits real
/// pages, so no browser will close it — and a page cannot close itself either, because
/// <c>window.close()</c> is a no-op for any tab the user did not open via script.
/// </para>
/// <para>
/// So this page does the only thing that is actually available: it performs the protocol handoff and
/// leaves the tab on a deliberate end state that says the sign-in worked and the tab can be closed.
/// </para>
/// </remarks>
public static class WindowsAuthCallbackPage
{
    /// <summary>
    /// The custom schemes the desktop app registers. Debug and release builds differ, so the scheme
    /// travels as the last path segment — and is checked against this list before it is ever written
    /// into a redirect, so a crafted link cannot turn this endpoint into an open redirect.
    /// </summary>
    /// <remarks>
    /// A path segment rather than a query parameter because Auth0 matches <c>redirect_uri</c> against
    /// the Allowed Callback URLs list exactly. A query string would have to be registered verbatim,
    /// which is both fragile and something Auth0's own guidance steers away from; one registered URL
    /// per scheme is unambiguous.
    /// </remarks>
    private static readonly HashSet<string> AllowedSchemes = new(StringComparer.Ordinal)
    {
        "harmonymusic", "harmonymusic-dev"
    };

    /// <summary>Legacy query parameter, still stripped so an older client's URL keeps working.</summary>
    public const string SchemeParameter = "scheme";
    private const string DefaultScheme = "harmonymusic";

    /// <summary>
    /// Builds the <c>scheme://callback?…</c> URL to hand back to, or null when <paramref name="scheme"/>
    /// is not one this app answers to.
    /// </summary>
    /// <param name="scheme">Requested custom scheme; null or empty selects the release scheme.</param>
    /// <param name="forwardedQuery">
    /// The incoming query string, with or without its leading <c>?</c>. Passed through untouched apart
    /// from dropping the routing-only <see cref="SchemeParameter"/> — the authorization code and state
    /// belong to the app and are never parsed, logged or stored here.
    /// </param>
    public static string? BuildRedirect(string? scheme, string? forwardedQuery)
    {
        var target = string.IsNullOrEmpty(scheme) ? DefaultScheme : scheme;
        if (!AllowedSchemes.Contains(target)) return null;

        var query = StripSchemeParameter(forwardedQuery);
        return query.Length == 0 ? $"{target}://callback" : $"{target}://callback?{query}";
    }

    /// <summary>The markup, compiled into the assembly. See <see cref="Template"/>.</summary>
    private const string TemplateResourceName = "WindowsAuthCallbackPage.html";

    /// <summary>Where the redirect goes in the markup, HTML-encoded and JS-escaped respectively.</summary>
    private const string HrefPlaceholder = "{{REDIRECT_HREF}}";
    private const string ScriptPlaceholder = "{{REDIRECT_JS}}";

    /// <summary>
    /// The page markup, read once from the embedded <c>WindowsAuthCallbackPage.html</c>.
    /// </summary>
    /// <remarks>
    /// An embedded resource rather than a file on disk: it travels inside the assembly, so it cannot
    /// go missing from a publish or be unreadable to the non-root container user, and there is no
    /// static-file middleware to add. Keeping it in a real <c>.html</c> file is what gives the markup
    /// syntax highlighting, formatting and validation, none of which a C# string literal gets.
    /// </remarks>
    private static readonly string Template = LoadTemplate();

    private static string LoadTemplate()
    {
        using var stream = typeof(WindowsAuthCallbackPage).Assembly
            .GetManifestResourceStream(TemplateResourceName)
            // A mis-wired csproj entry would otherwise surface as a blank page in production. Name
            // the resource so the cause is obvious rather than having to guess at an empty response.
            ?? throw new InvalidOperationException(
                $"Embedded resource '{TemplateResourceName}' is missing. It must be declared as an " +
                "<EmbeddedResource> with a matching LogicalName in Harmony.Cloud.Api.csproj.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// The self-contained HTML served for a valid callback. No external assets: a strict page that
    /// works offline is also one that cannot leak the query string to a third party.
    /// </summary>
    public static string Render(string redirect)
    {
        // The URL is attribute- and script-escaped separately. It is already constrained to an
        // allowlisted scheme, but the query string riding on it is attacker-reachable input.
        var attribute = WebUtility.HtmlEncode(redirect);
        var script = redirect.Replace("\\", "\\\\").Replace("'", "\\'").Replace("<", "\\u003c");
        return Template
            .Replace(HrefPlaceholder, attribute)
            .Replace(ScriptPlaceholder, script);
    }

    private static string StripSchemeParameter(string? forwardedQuery)
    {
        if (string.IsNullOrEmpty(forwardedQuery)) return string.Empty;
        var trimmed = forwardedQuery.StartsWith('?') ? forwardedQuery[1..] : forwardedQuery;
        if (trimmed.Length == 0) return string.Empty;

        var kept = new StringBuilder();
        foreach (var pair in trimmed.Split('&'))
        {
            if (pair.Length == 0) continue;
            var nameLength = pair.IndexOf('=');
            var name = nameLength < 0 ? pair : pair[..nameLength];
            if (string.Equals(name, SchemeParameter, StringComparison.Ordinal)) continue;
            if (kept.Length > 0) kept.Append('&');
            kept.Append(pair);
        }
        return kept.ToString();
    }
}
