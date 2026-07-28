namespace SignalRMVC.FreezeMonitor.Models;

public sealed record BrowserDiagnosticsPayload(
    DateTimeOffset TimestampUtc,
    string ClientId,
    string Url,
    string UserAgent,
    int UnhandledErrorsCount,
    int PromiseRejectionsCount,
    int AjaxFailuresCount,
    int ConsecutiveHeartbeatFailures,
    string SignalRState,
    bool IsOnline,
    string VisibilityState,
    object? PerformanceTiming,
    object? MemoryInfo,
    List<string>? RecentErrorLogs
);
