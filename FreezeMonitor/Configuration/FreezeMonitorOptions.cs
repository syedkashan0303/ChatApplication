namespace SignalRMVC.FreezeMonitor.Configuration;

public sealed class FreezeMonitorOptions
{
    public const string SectionName = "FreezeMonitor";

    public bool Enabled { get; set; } = true;
    public int SamplingIntervalSeconds { get; set; } = 10;
    public int SqlHealthIntervalSeconds { get; set; } = 30;
    public int SqlHealthTimeoutSeconds { get; set; } = 5;
    public int RingBufferSize { get; set; } = 500;
    public int LongRequestThresholdSeconds { get; set; } = 5;
    public int MaxLongRequests { get; set; } = 50;
    public string IncidentFolder { get; set; } = "Incidents";
    public bool EnableDiagnosticsApi { get; set; } = true;

    public FreezeDetectionOptions FreezeDetection { get; set; } = new();
    public ProcDumpOptions ProcDump { get; set; } = new();
    public SmtpOptions SMTP { get; set; } = new();
}
