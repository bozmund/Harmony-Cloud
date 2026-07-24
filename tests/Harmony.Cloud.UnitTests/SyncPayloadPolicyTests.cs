using System.Text.Json;
using Harmony.Cloud.Api.Domain;
using Xunit;

namespace Harmony.Cloud.UnitTests;

public sealed class SyncPayloadPolicyTests
{
    [Fact]
    public void Song_entity_retains_only_its_video_id()
    {
        using var payload = JsonDocument.Parse("{\"title\":\"Song\",\"artist\":\"Artist\"}");
        using var normalized = SyncPayloadPolicy.Normalize("song", "abcdefghijk", payload.RootElement);

        Assert.Equal("abcdefghijk", normalized.RootElement.GetProperty("videoId").GetString());
        Assert.Single(normalized.RootElement.EnumerateObject());
    }

    [Fact]
    public void User_owned_playlist_state_keeps_its_name_but_redacts_embedded_song_metadata()
    {
        using var payload = JsonDocument.Parse("""
            {"title":"Road trip","items":[{"position":1,"videoId":"abcdefghijk","title":"Song","artist":"Artist","durationMs":120000}]}
            """);
        using var normalized = SyncPayloadPolicy.Normalize("playlist", "playlist-1", payload.RootElement);

        Assert.Equal("Road trip", normalized.RootElement.GetProperty("title").GetString());
        var item = normalized.RootElement.GetProperty("items")[0];
        Assert.Equal("abcdefghijk", item.GetProperty("videoId").GetString());
        Assert.Equal(1, item.GetProperty("position").GetInt32());
        Assert.False(item.TryGetProperty("title", out _));
        Assert.False(item.TryGetProperty("artist", out _));
        Assert.False(item.TryGetProperty("durationMs", out _));
    }

    [Fact]
    public void Invalid_song_identifier_is_rejected()
    {
        using var payload = JsonDocument.Parse("{}");

        var exception = Assert.Throws<InvalidDataException>(() =>
            SyncPayloadPolicy.Normalize("track", "not-a-video-id", payload.RootElement));

        Assert.Equal("invalid_video_id", exception.Message);
    }
}
