using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Harmony.Cloud.Api.Domain;

/// <summary>Prevents Cloud from becoming a duplicate song catalog.</summary>
public static partial class SyncPayloadPolicy
{
    private static readonly HashSet<string> SongMetadataProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "title", "artist", "artists", "album", "albumArtist", "artwork", "artworkUrl", "thumbnail",
        "description", "duration", "durationMs", "format", "fileSize", "mimeType", "streamUrl",
        "audioUrl", "mediaUrl", "channel", "publishedAt"
    };

    public static JsonDocument Normalize(string entityType, string entityId, JsonElement payload)
    {
        if (IsSongEntity(entityType))
        {
            if (!IsVideoId(entityId)) throw new InvalidDataException("invalid_video_id");
            return JsonDocument.Parse(JsonSerializer.Serialize(new { videoId = entityId }));
        }

        var node = JsonNode.Parse(payload.GetRawText()) ?? throw new InvalidDataException("invalid_sync_payload");
        RedactSongMetadata(node);
        return JsonDocument.Parse(node.ToJsonString());
    }

    public static bool IsVideoId(string value) => VideoIdPattern().IsMatch(value);

    private static bool IsSongEntity(string entityType) =>
        string.Equals(entityType, "song", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(entityType, "track", StringComparison.OrdinalIgnoreCase);

    private static void RedactSongMetadata(JsonNode node)
    {
        switch (node)
        {
            case JsonArray array:
                foreach (var item in array.Where(item => item is not null)) RedactSongMetadata(item!);
                return;
            case JsonObject objectNode:
                RedactObject(objectNode);
                return;
            default:
                return;
        }
    }

    private static void RedactObject(JsonObject objectNode)
    {
        var videoId = objectNode["videoId"]?.GetValue<string>();
        if (videoId is not null && !IsVideoId(videoId)) throw new InvalidDataException("invalid_video_id");
        var isSongReference = videoId is not null;
        foreach (var property in objectNode.ToArray())
        {
            if (isSongReference && SongMetadataProperties.Contains(property.Key))
            {
                objectNode.Remove(property.Key);
                continue;
            }
            if (property.Value is not null) RedactSongMetadata(property.Value);
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{11}$", RegexOptions.CultureInvariant)]
    private static partial Regex VideoIdPattern();
}
