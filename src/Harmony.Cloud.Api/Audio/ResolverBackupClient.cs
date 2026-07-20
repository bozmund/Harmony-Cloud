using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Harmony.Cloud.Api.Configuration;

namespace Harmony.Cloud.Api.Audio;

public sealed class ResolverBackupClient(HttpClient http, CloudOptions options)
{
    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    private string? _token;
    private DateTimeOffset _tokenExpiresAt;

    public async Task<BackupPlan> NextAsync(
        IReadOnlyList<string> videoIds,
        CancellationToken cancellationToken)
    {
        foreach (var videoId in videoIds.Distinct(StringComparer.Ordinal))
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(videoId, "^[A-Za-z0-9_-]{11}$"))
                continue;
            using var request = new HttpRequestMessage(
                HttpMethod.Post, new Uri(options.ResolverBaseUrl, "internal/v1/backup-grants"))
            {
                Content = JsonContent.Create(new { videoId })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer", await GetTokenAsync(cancellationToken));
            using var response = await http.SendAsync(request, cancellationToken);
            if ((int)response.StatusCode == 507)
                return new BackupPlan("deferred_capacity", videoId, null, null);
            response.EnsureSuccessStatusCode();
            var grant = await response.Content.ReadFromJsonAsync<ResolverGrant>(
                cancellationToken: cancellationToken)
                ?? throw new InvalidDataException("Resolver returned an empty backup grant.");
            if (grant.Status == "upload")
                return new BackupPlan(
                    "upload", videoId,
                    new Uri(new Uri("https://harmony-resolver.duckdns.org"), grant.UploadPath!).ToString(),
                    grant.UploadToken);
        }
        return new BackupPlan("none", null, null, null);
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (_token is not null && _tokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
            return _token;
        await _tokenGate.WaitAsync(cancellationToken);
        try
        {
            if (_token is not null && _tokenExpiresAt > DateTimeOffset.UtcNow.AddMinutes(1))
                return _token;
            using var response = await http.PostAsJsonAsync(
                $"https://{options.Auth0Domain.TrimEnd('/')}/oauth/token",
                new
                {
                    client_id = options.Auth0ClientId,
                    client_secret = options.Auth0ClientSecret,
                    audience = options.Auth0Audience,
                    grant_type = "client_credentials"
                }, cancellationToken);
            response.EnsureSuccessStatusCode();
            var token = await response.Content.ReadFromJsonAsync<TokenResponse>(
                cancellationToken: cancellationToken)
                ?? throw new InvalidDataException("Auth0 returned an empty token.");
            _token = token.AccessToken;
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn);
            return _token;
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    private sealed record ResolverGrant(
        string Status, string VideoId, Guid? UploadId, string? UploadPath, string? UploadToken);
    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}

public sealed record BackupPlan(
    string Status, string? VideoId, string? UploadUrl, string? UploadToken);
public sealed record AudioNextRequest(Guid DeviceId, IReadOnlyList<string> VideoIds);
