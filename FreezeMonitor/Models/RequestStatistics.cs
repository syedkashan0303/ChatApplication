namespace SignalRMVC.FreezeMonitor.Models;

public sealed record RequestStatistics(
    int CurrentRequests,
    int PendingRequests,
    long CompletedRequests,
    long FailedRequests,
    long TotalRequests,
    double LongestRequestMilliseconds,
    double AverageResponseTimeMilliseconds,
    double RequestsPerMinute,
    string? LastError,
    IReadOnlyList<LongRunningRequest> LongRunningRequests);
