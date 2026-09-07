namespace CoHAnalytics.Models;

public sealed class MonitoringSessionManagerChangedEventArgs : EventArgs
{
    public MonitoringSessionManagerChangedEventArgs(MonitoringSessionManagerSnapshot snapshot)
    {
        Snapshot = snapshot;
    }

    public MonitoringSessionManagerSnapshot Snapshot { get; }
}
