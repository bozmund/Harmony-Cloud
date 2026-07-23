using Harmony.Cloud.Api.Configuration;
using Harmony.Cloud.Api.Playback;
using Xunit;

namespace Harmony.Cloud.UnitTests;

public sealed class FcmWakeupServiceTests
{
    [Fact]
    public void Push_registration_is_encrypted_and_can_be_used_without_storing_plaintext()
    {
        var service = new FcmWakeupService(new CloudOptions { IdentityHmacKey = new string('a', 32) });

        var ciphertext = service.Protect("fcm-registration-token");

        Assert.DoesNotContain("fcm-registration-token", ciphertext, StringComparison.Ordinal);
        Assert.NotEmpty(ciphertext);
    }
}
