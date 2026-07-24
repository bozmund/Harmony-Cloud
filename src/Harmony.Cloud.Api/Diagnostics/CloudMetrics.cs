using System.Diagnostics.Metrics;

namespace Harmony.Cloud.Api.Diagnostics;

public sealed class CloudMetrics
{
    public const string MeterName = "Harmony.Cloud.Api";
    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _acceptedSyncEvents;

    public CloudMetrics()
    {
        _acceptedSyncEvents = _meter.CreateCounter<long>("harmony.cloud.sync.events.accepted");
    }

    public void RecordAcceptedSyncEvents(int count)
    {
        if (count > 0) _acceptedSyncEvents.Add(count);
    }
}
