namespace SignalRMVC.FreezeMonitor.Models;

public sealed record HealthSnapshot(
    DateTimeOffset TimestampUtc,
    string MachineName,
    DateTimeOffset ServerTime,
    int ProcessId,
    double ApplicationUptimeSeconds,
    double CpuUsagePercent,
    long WorkingSetBytes,
    long PrivateMemoryBytes,
    long ManagedMemoryBytes,
    int ThreadCount,
    int HandleCount,
    int ActiveSignalRConnections,
    int ConnectedUsers,
    SqlHealthSnapshot Sql,
    ThreadPoolSnapshot ThreadPool,
    int PendingRequests,
    int LongRunningRequests,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    string? LastError);
