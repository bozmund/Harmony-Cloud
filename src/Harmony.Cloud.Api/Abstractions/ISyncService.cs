using Harmony.Cloud.Api.Sync;

namespace Harmony.Cloud.Api.Abstractions;

public interface ISyncService
{
    Task<SyncResponse> SyncAsync(string accountId, SyncRequest request, CancellationToken cancellationToken);
}
