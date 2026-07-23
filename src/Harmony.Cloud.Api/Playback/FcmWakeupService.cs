using System.Security.Cryptography;
using System.Text;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Harmony.Cloud.Api.Configuration;

namespace Harmony.Cloud.Api.Playback;

/// Stores FCM registrations encrypted at rest and sends an opaque command id.
/// The device fetches the real command through its own authenticated Cloud API call.
public sealed class FcmWakeupService(CloudOptions options)
{
    private readonly byte[] _key = SHA256.HashData(Encoding.UTF8.GetBytes(options.IdentityHmacKey));
    private readonly FirebaseMessaging? _messaging = CreateMessaging(options.FcmServiceAccountJsonBase64);

    public string Protect(string token)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintext = Encoding.UTF8.GetBytes(token);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);
        return Convert.ToBase64String(nonce.Concat(tag).Concat(ciphertext).ToArray());
    }

    public async Task WakeAsync(string? ciphertext, Guid commandId, CancellationToken cancellationToken)
    {
        if (_messaging is null || string.IsNullOrWhiteSpace(ciphertext)) return;
        try
        {
            await _messaging.SendAsync(new Message
            {
                Token = Unprotect(ciphertext),
                Data = new Dictionary<string, string> { ["commandId"] = commandId.ToString("N") },
                Android = new AndroidConfig { Priority = Priority.High }
            }, cancellationToken);
        }
        catch (FirebaseMessagingException)
        {
            // Delivery is best effort. The command expiry/acknowledgement is authoritative.
        }
    }

    private string Unprotect(string encoded)
    {
        var data = Convert.FromBase64String(encoded);
        if (data.Length < 29) throw new CryptographicException("Invalid push registration.");
        var plaintext = new byte[data.Length - 28];
        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(data[..12], data[12..28], data[28..], plaintext);
        return Encoding.UTF8.GetString(plaintext);
    }

    private static FirebaseMessaging? CreateMessaging(string encodedServiceAccount)
    {
        if (string.IsNullOrWhiteSpace(encodedServiceAccount)) return null;
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(encodedServiceAccount));
        var app = FirebaseApp.Create(new AppOptions { Credential = GoogleCredential.FromJson(json) }, "harmony-cloud-fcm");
        return FirebaseMessaging.GetMessaging(app);
    }
}
