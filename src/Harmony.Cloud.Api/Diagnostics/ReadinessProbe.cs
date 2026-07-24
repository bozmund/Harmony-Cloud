using Harmony.Cloud.Api.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Harmony.Cloud.Api.Diagnostics;

public sealed class ReadinessProbe(IDbContextFactory<CloudDbContext> contexts)
{
    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.Database.CanConnectAsync(cancellationToken);
    }
}
