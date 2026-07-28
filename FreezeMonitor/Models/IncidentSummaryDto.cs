namespace SignalRMVC.FreezeMonitor.Models;

public sealed record IncidentSummaryDto(
    string IncidentId,
    DateTimeOffset TimestampUtc,
    string Reason,
    string Severity,
    string MachineName,
    int ProcessId,
    string DirectoryPath,
    List<string> CapturedFiles,
    HealthStatusLevel StatusLevel
);
