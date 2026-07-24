using System.Text.Json;
using Harmony.Cloud.Api.Domain;
using Xunit;

namespace Harmony.Cloud.UnitTests;

public sealed class PlaybackPayloadTests
{
    [Theory]
    [InlineData("{\"title\":\"A song\",\"queue\":[{\"videoId\":\"abcdefghijk\"}]}", true)]
    [InlineData("{\"streamUrl\":\"https://example.test/audio\"}", false)]
    [InlineData("{\"localPath\":\"C:/music/song.ogg\"}", false)]
    [InlineData("{\"nested\":[{\"accessToken\":\"secret\"}]}", false)]
    public void Portable_payloads_are_bounded_and_do_not_contain_sensitive_location_data(string json, bool expected)
    {
        using var document = JsonDocument.Parse(json);

        Assert.Equal(expected, PlaybackPayload.IsPortable(document.RootElement));
    }
}
