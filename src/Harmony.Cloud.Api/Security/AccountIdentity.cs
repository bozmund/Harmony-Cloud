using System.Security.Cryptography;
using System.Text;
using Harmony.Cloud.Api.Configuration;

namespace Harmony.Cloud.Api.Security;

public sealed class AccountIdentity(CloudOptions options)
{
    public string Resolve(HttpContext context)
    {
        var subject = context.User.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(subject) && context.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment())
            subject = context.Request.Headers["X-Test-Subject"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(subject))
            throw new UnauthorizedAccessException("An authenticated subject is required.");

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(options.IdentityHmacKey));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(subject))).ToLowerInvariant();
    }
}
