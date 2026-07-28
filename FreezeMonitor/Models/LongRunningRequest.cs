namespace SignalRMVC.FreezeMonitor.Models;

public sealed record LongRunningRequest(
    string RequestId,
    string Method,
    string Path,
    string? UserName,
    string ConnectionId,
    int ThreadId,
    DateTimeOffset StartedAtUtc,
    double ElapsedMilliseconds);
