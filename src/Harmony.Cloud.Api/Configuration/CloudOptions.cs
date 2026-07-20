namespace Harmony.Cloud.Api.Configuration;

public sealed class CloudOptions
{
    public required string IdentityHmacKey { get; init; }
    public Uri ResolverBaseUrl { get; init; } = new("http://resolver-api:8080/");
    public int MaxEventsPerSync { get; init; } = 500;
    public TimeSpan UploadGrantLifetime { get; init; } = TimeSpan.FromMinutes(30);
    public string Auth0Domain { get; init; } = "";
    public string Auth0ClientId { get; init; } = "";
    public string Auth0ClientSecret { get; init; } = "";
    public string Auth0Audience { get; init; } = "https://harmony-api";
}
