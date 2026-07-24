using Harmony.Cloud.Api.Audio;

namespace Harmony.Cloud.Api.Endpoints;

public static class AudioEndpoints
{
    public static RouteGroupBuilder MapAudioEndpoints(this RouteGroupBuilder cloud)
    {
        cloud.MapPost("/audio/next", NextAsync);
        return cloud;
    }

    private static async Task<IResult> NextAsync(
        AudioNextRequest request, ResolverBackupClient resolver, CancellationToken cancellationToken)
    {
        if (request.DeviceId == Guid.Empty || request.VideoIds.Count is < 1 or > 500)
            return Results.BadRequest(new { code = "invalid_audio_inventory" });
        return Results.Ok(await resolver.NextAsync(request.VideoIds, cancellationToken));
    }
}
