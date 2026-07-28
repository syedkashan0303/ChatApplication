namespace SignalRMVC.FreezeMonitor.Models;

public sealed record IncidentReport(
    DateTimeOffset TimestampUtc,
    string Reason,
    string DirectoryPath,
    string MachineName,
    int ProcessId,
    HealthSnapshot Health,
    RequestStatistics Statistics,
    SignalRSnapshot SignalR,
    ThreadPoolSnapshot ThreadPool,
    SqlHealthSnapshot Sql);
