using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using Harmony.Cloud.Api.Configuration;

namespace Harmony.Cloud.Api.Security;

public sealed class AccountIdentity(CloudOptions options)
{
    public string Resolve(HttpContext context)
    {
        // JwtBearer can preserve Auth0's `sub` claim or map it to the
        // standard .NET name-identifier claim. Both represent the same
        // authenticated subject and must derive the same opaque account id.
        var subject = context.User.FindFirst("sub")?.Value
            ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(subject) && context.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment())
            subject = context.Request.Headers["X-Test-Subject"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(subject))
            throw new UnauthorizedAccessException("An authenticated subject is required.");

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(options.IdentityHmacKey));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(subject))).ToLowerInvariant();
    }
}
