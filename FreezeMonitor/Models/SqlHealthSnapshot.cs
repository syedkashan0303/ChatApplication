namespace SignalRMVC.FreezeMonitor.Models;

public sealed record SqlHealthSnapshot(
    DateTimeOffset TimestampUtc,
    bool IsHealthy,
    double ConnectionTimeMilliseconds,
    double ExecutionTimeMilliseconds,
    double TotalTimeMilliseconds,
    bool TimedOut,
    string? ErrorMessage);
