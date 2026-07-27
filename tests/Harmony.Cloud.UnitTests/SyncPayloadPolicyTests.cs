using System.Text.Json;
using Harmony.Cloud.Api.Domain;
using Xunit;

namespace Harmony.Cloud.UnitTests;

public sealed class SyncPayloadPolicyTests
{
    [Fact]
    public void Song_display_metadata_survives_normalization()
    {
        using var payload = JsonDocument.Parse("""
            {"videoId":"abcdefghijk","title":"Song","artists":[{"name":"Artist"}],"album":{"name":"Album"},
             "duration":215,"year":2019,"thumbnails":[{"url":"https://lh3.googleusercontent.com/cover"}]}
            """);
        using var normalized = SyncPayloadPolicy.Normalize("hive-entry", "SongDownloads:key", payload.RootElement);

        var root = normalized.RootElement;
        Assert.Equal("Song", root.GetProperty("title").GetString());
        Assert.Equal("Artist", root.GetProperty("artists")[0].GetProperty("name").GetString());
        Assert.Equal("Album", root.GetProperty("album").GetProperty("name").GetString());
        Assert.Equal(215, root.GetProperty("duration").GetInt32());
        Assert.Equal(2019, root.GetProperty("year").GetInt32());
        Assert.Equal(
            "https://lh3.googleusercontent.com/cover",
            root.GetProperty("thumbnails")[0].GetProperty("url").GetString());
    }

    [Fact]
    public void Stream_and_local_location_fields_are_stripped()
    {
        using var payload = JsonDocument.Parse("""
            {"videoId":"abcdefghijk","title":"Song","url":"/storage/emulated/0/Music/song.m4a",
             "streamUrl":"https://example.invalid/stream","streamInfo":{"url":"https://example.invalid/x"},
             "filePath":"C:/Music/song.m4a","localPath":"/data/song.m4a","downloadPath":"/downloads"}
            """);
        using var normalized = SyncPayloadPolicy.Normalize("hive-entry", "SongDownloads:key", payload.RootElement);

        var root = normalized.RootElement;
        Assert.Equal("Song", root.GetProperty("title").GetString());
        foreach (var stripped in new[] { "url", "streamUrl", "streamInfo", "filePath", "localPath", "downloadPath" })
            Assert.False(root.TryGetProperty(stripped, out _), stripped);
    }

    [Fact]
    public void Playlist_state_keeps_its_name_and_embedded_song_metadata()
    {
        using var payload = JsonDocument.Parse("""
            {"title":"Road trip","items":[{"position":1,"videoId":"abcdefghijk","title":"Song","artist":"Artist","durationMs":120000}]}
            """);
        using var normalized = SyncPayloadPolicy.Normalize("playlist", "playlist-1", payload.RootElement);

        Assert.Equal("Road trip", normalized.RootElement.GetProperty("title").GetString());
        var item = normalized.RootElement.GetProperty("items")[0];
        Assert.Equal("abcdefghijk", item.GetProperty("videoId").GetString());
        Assert.Equal(1, item.GetProperty("position").GetInt32());
        Assert.Equal("Song", item.GetProperty("title").GetString());
        Assert.Equal("Artist", item.GetProperty("artist").GetString());
        Assert.Equal(120000, item.GetProperty("durationMs").GetInt32());
    }

    [Fact]
    public void Invalid_song_entity_identifier_is_rejected()
    {
        using var payload = JsonDocument.Parse("{}");

        var exception = Assert.Throws<InvalidDataException>(() =>
            SyncPayloadPolicy.Normalize("track", "not-a-video-id", payload.RootElement));

        Assert.Equal("invalid_video_id", exception.Message);
    }

    [Fact]
    public void Invalid_embedded_video_id_is_rejected()
    {
        using var payload = JsonDocument.Parse("""{"videoId":"not-a-video-id","title":"Song"}""");

        var exception = Assert.Throws<InvalidDataException>(() =>
            SyncPayloadPolicy.Normalize("hive-entry", "SongDownloads:key", payload.RootElement));

        Assert.Equal("invalid_video_id", exception.Message);
    }
}
