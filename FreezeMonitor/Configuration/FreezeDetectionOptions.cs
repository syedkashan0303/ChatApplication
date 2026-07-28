namespace SignalRMVC.FreezeMonitor.Configuration;

public sealed class FreezeDetectionOptions
{
    public bool Enabled { get; set; } = true;
    public int NoCompletedRequestSeconds { get; set; } = 30;
    public int PendingRequestThreshold { get; set; } = 20;
    public int ThreadPoolWorkerThreshold { get; set; } = 5;
    public int SqlFailureThreshold { get; set; } = 3;
    public int HeartbeatFailureThreshold { get; set; } = 3;
}
