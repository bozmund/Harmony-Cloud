namespace Harmony.Cloud.Api.Configuration;

public sealed class CloudOptions
{
    public required string IdentityHmacKey { get; init; }
    public int MaxEventsPerSync { get; init; } = 500;
    public string FcmServiceAccountJsonBase64 { get; init; } = "";
}
