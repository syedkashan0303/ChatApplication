using Microsoft.Extensions.Options;
using SignalRMVC.FreezeMonitor.Configuration;
using SignalRMVC.FreezeMonitor.Models;

namespace SignalRMVC.FreezeMonitor.Services;

public sealed record DiagnosticIssue(
    string Key,
    string Severity,
    string Category,
    string Title,
    string Detail,
    string Suggestion,
    DateTimeOffset FirstSeenUtc);

public sealed record IssueHistoryEntry(
    DateTimeOffset TimestampUtc,
    string Event,
    string Severity,
    string Category,
    string Title);

/// <summary>
/// Turns raw health data (ring buffer, request stats, hub/DB trackers) into a list of human-readable issues.
/// Analyze() is pure; Tick() (called by the monitor loop) also records when issues open and resolve.
/// </summary>
public sealed class DiagnosticsAnalyzer
{
    public const string Critical = "Critical";
    public const string Warning = "Warning";
    public const string Info = "Info";

    private const int MaxHistory = 100;

    private readonly RingBufferService _ringBuffer;
    private readonly RequestStatisticsService _requests;
    private readonly SignalRHealthService _signalR;
    private readonly HubDiagnosticsService _hub;
    private readonly DbDiagnosticsService _db;
    private readonly FreezeDetectionService _freezeDetection;
    private readonly IOptionsMonitor<FreezeMonitorOptions> _options;
    private readonly ILogger _logger;

    private readonly object _sync = new();
    private readonly Dictionary<string, DiagnosticIssue> _active = new(StringComparer.Ordinal);
    private readonly List<IssueHistoryEntry> _history = new();

    public DiagnosticsAnalyzer(
        RingBufferService ringBuffer,
        RequestStatisticsService requests,
        SignalRHealthService signalR,
        HubDiagnosticsService hub,
        DbDiagnosticsService db,
        FreezeDetectionService freezeDetection,
        IOptionsMonitor<FreezeMonitorOptions> options,
        ILoggerFactory loggerFactory)
    {
        _ringBuffer = ringBuffer;
        _requests = requests;
        _signalR = signalR;
        _hub = hub;
        _db = db;
        _freezeDetection = freezeDetection;
        _options = options;
        _logger = loggerFactory.CreateLogger("FreezeMonitor");
    }

    // Called by the monitor loop: refresh the active set and record opened / resolved issues.
    public void Tick()
    {
        var current = Analyze();
        var now = DateTimeOffset.UtcNow;

        lock (_sync)
        {
            var currentKeys = current.Select(i => i.Key).ToHashSet(StringComparer.Ordinal);

            foreach (var issue in current)
            {
                if (_active.ContainsKey(issue.Key))
                {
                    // keep first-seen time, but refresh the text (numbers change)
                    _active[issue.Key] = issue with { FirstSeenUtc = _active[issue.Key].FirstSeenUtc };
                    continue;
                }

                _active[issue.Key] = issue with { FirstSeenUtc = now };
                AddHistory(new IssueHistoryEntry(now, "Opened", issue.Severity, issue.Category, issue.Title));

                if (issue.Severity == Critical)
                    _logger.LogError("Diagnostics issue opened [{Category}] {Title}: {Detail}", issue.Category, issue.Title, issue.Detail);
                else
                    _logger.LogWarning("Diagnostics issue opened [{Category}] {Title}: {Detail}", issue.Category, issue.Title, issue.Detail);
            }

            foreach (var key in _active.Keys.Where(k => !currentKeys.Contains(k)).ToList())
            {
                var resolved = _active[key];
                _active.Remove(key);
                AddHistory(new IssueHistoryEntry(now, "Resolved", resolved.Severity, resolved.Category, resolved.Title));
            }
        }
    }

    public IReadOnlyList<DiagnosticIssue> GetCurrentIssues()
    {
        var current = Analyze();
        lock (_sync)
        {
            return current
                .Select(i => _active.TryGetValue(i.Key, out var known) ? i with { FirstSeenUtc = known.FirstSeenUtc } : i)
                .ToList();
        }
    }

    public IReadOnlyList<IssueHistoryEntry> GetHistory()
    {
        lock (_sync)
        {
            return _history.AsEnumerable().Reverse().ToList();
        }
    }

    public IReadOnlyList<DiagnosticIssue> Analyze()
    {
        var now = DateTimeOffset.UtcNow;
        var options = _options.CurrentValue;
        var issues = new List<DiagnosticIssue>();

        void Add(string key, string severity, string category, string title, string detail, string suggestion) =>
            issues.Add(new DiagnosticIssue(key, severity, category, title, detail, suggestion, now));

        var latest = _ringBuffer.GetLatest();
        var recent = _ringBuffer.GetLatest(30); // ~5 minutes at the default 10s sampling
        var requestStats = _requests.GetStatistics(
            TimeSpan.FromSeconds(Math.Max(1, options.LongRequestThresholdSeconds)),
            options.MaxLongRequests);
        var signalR = _signalR.GetSnapshot();
        var hub = _hub.GetSnapshot();
        var db = _db.GetSnapshot();

        // ---------- Monitor itself ----------
        if (latest == null)
        {
            Add("monitor-nodata", Info, "Monitor", "No health samples yet",
                "The monitor has not collected its first sample.", "Wait one sampling interval after startup.");
        }
        else
        {
            var lag = (now - latest.TimestampUtc).TotalSeconds;
            if (lag > Math.Max(30, options.SamplingIntervalSeconds * 3))
            {
                Add("monitor-stale", Critical, "Monitor", "Monitoring has stopped sampling",
                    $"Last health sample is {lag:F0}s old (expected every {options.SamplingIntervalSeconds}s). The app, or its thread pool, may be frozen.",
                    "Check the server immediately: CPU, thread pool starvation, or a blocked process. Capture an incident.");
            }
        }

        var level = _freezeDetection.CurrentStatusLevel;
        if (level >= HealthStatusLevel.Critical)
        {
            Add("freeze-level", level == HealthStatusLevel.FreezeDetected ? Critical : Warning, "Freeze Monitor",
                $"Freeze monitor status: {level}",
                "The built-in freeze rules (SQL failures, pending requests, thread pool, stalled requests) are tripped.",
                "See the incident history below for the captured bundle and reasons.");
        }

        // ---------- Database ----------
        if (latest != null)
        {
            var sql = latest.Sql;
            if (!sql.IsHealthy)
            {
                Add("db-down", Critical, "Database", sql.TimedOut ? "Database health check timed out" : "Database health check failed",
                    sql.ErrorMessage ?? "No details.",
                    "Check SQL Server is up, reachable from this machine, and not blocked/overloaded. Chat will fail while this persists.");
            }
            else if (sql.TotalTimeMilliseconds >= 2000)
            {
                Add("db-latency", Critical, "Database", "Database responds very slowly",
                    $"A trivial 'SELECT 1' took {sql.TotalTimeMilliseconds:F0} ms (connect {sql.ConnectionTimeMilliseconds:F0} ms).",
                    "Check SQL Server load, blocking sessions, network latency and connection pool exhaustion.");
            }
            else if (sql.TotalTimeMilliseconds >= 500)
            {
                Add("db-latency", Warning, "Database", "Database latency is elevated",
                    $"A trivial 'SELECT 1' took {sql.TotalTimeMilliseconds:F0} ms (connect {sql.ConnectionTimeMilliseconds:F0} ms).",
                    "Healthy is under ~50 ms. Look for blocking, the daily archive job overlapping, or network issues.");
            }
        }

        if (db.SlowLast10Min >= 3)
        {
            var worst = db.Queries.OrderByDescending(q => q.MaxMs).FirstOrDefault();
            Add("db-slow-queries", db.SlowLast10Min >= 20 ? Critical : Warning, "Database",
                $"{db.SlowLast10Min} slow database queries in the last 10 minutes",
                worst == null ? $"Queries over {DbDiagnosticsService.SlowThresholdMs:F0} ms." :
                    $"Slowest: {worst.MaxMs:F0} ms (avg {worst.AverageMs:F0} ms): {Truncate(worst.Sql, 160)}",
                "Open the 'Database queries' table below. Slow filters on unindexed columns (GroupName, SenderId, ClientMessageId) are the usual cause.");
        }

        if (db.FailedLast10Min > 0)
        {
            var lastFailure = db.RecentEvents.FirstOrDefault(e => e.Kind == "failed");
            Add("db-failed-queries", db.FailedLast10Min >= 5 ? Critical : Warning, "Database",
                $"{db.FailedLast10Min} database command(s) failed in the last 10 minutes",
                lastFailure?.Message ?? "See logs.",
                "Check for timeouts, deadlocks or constraint violations in the log file.");
        }

        // ---------- Hub ----------
        if (hub.ErrorsLast10Min > 0)
        {
            var worstMethod = hub.Methods.OrderByDescending(m => m.Errors).FirstOrDefault(m => m.Errors > 0);
            Add("hub-errors", hub.ErrorsLast10Min >= 10 ? Critical : Warning, "SignalR Hub",
                $"{hub.ErrorsLast10Min} hub error(s) in the last 10 minutes",
                worstMethod == null ? "See recent hub events." :
                    $"Most errors: {worstMethod.Method} ({worstMethod.Errors} total). Last: {worstMethod.LastError}",
                "Open 'Recent hub events' below. Handled errors (message not saved) mean users may be losing messages.");
        }

        if (hub.SlowLast10Min >= 3)
        {
            var slowMethods = hub.RecentEvents.Where(e => e.Kind == "slow").Select(e => e.Method).Distinct().Take(4);
            Add("hub-slow", hub.SlowLast10Min >= 20 ? Critical : Warning, "SignalR Hub",
                $"{hub.SlowLast10Min} slow hub call(s) (over {HubDiagnosticsService.SlowThresholdMs:F0} ms) in the last 10 minutes",
                $"Methods: {string.Join(", ", slowMethods)}.",
                "Hub calls this slow usually mean a slow DB query or a blocked thread inside the method.");
        }

        foreach (var method in hub.Methods.Where(m => m.Calls >= 10 && m.AverageMs >= 500))
        {
            Add($"hub-method-avg-{method.Method}", Warning, "SignalR Hub",
                $"Hub method {method.Method} is slow on average",
                $"Average {method.AverageMs:F0} ms over {method.Calls} calls (max {method.MaxMs:F0} ms).",
                "Profile this method; it runs on every use and will not scale to many users.");
        }

        if (hub.AbnormalDisconnectsLast10Min >= 5)
        {
            var lastDisconnect = hub.RecentEvents.FirstOrDefault(e => e.Kind == "disconnect");
            Add("hub-disconnects", hub.AbnormalDisconnectsLast10Min >= 20 ? Critical : Warning, "SignalR Hub",
                $"{hub.AbnormalDisconnectsLast10Min} clients dropped abnormally in the last 10 minutes",
                $"Last reason: {lastDisconnect?.Message ?? "unknown"}",
                "Check network/proxy idle timeouts, WebSocket support, server restarts, and the 60s client timeout.");
        }

        var churnThreshold = Math.Max(30, signalR.ConnectedUsers * 2);
        if (hub.ConnectsLast5Min >= churnThreshold)
        {
            Add("hub-churn", Warning, "SignalR Hub",
                $"High reconnect churn: {hub.ConnectsLast5Min} new connections in 5 minutes",
                $"Only {signalR.ConnectedUsers} user(s) connected right now.",
                "Clients are reconnecting repeatedly. Look for network flaps, expired sessions, or a reconnect loop in the page.");
        }

        if (signalR.CurrentConnections >= 10 && signalR.CurrentConnections > signalR.ConnectedUsers * 3)
        {
            Add("hub-conn-per-user", Info, "SignalR Hub",
                "Many connections per user",
                $"{signalR.CurrentConnections} connections for {signalR.ConnectedUsers} users.",
                "Users with many tabs open, or old connections not being closed.");
        }

        if (hub.AuthFailuresLast10Min >= 3)
        {
            Add("hub-auth", Warning, "SignalR Hub",
                $"{hub.AuthFailuresLast10Min} unauthenticated hub call(s) in the last 10 minutes",
                "Hub methods were invoked without a valid login cookie.",
                "Usually expired sessions in open tabs. If it keeps growing it may be someone probing the hub.");
        }

        // ---------- HTTP requests ----------
        if (requestStats.PendingRequests >= options.FreezeDetection.PendingRequestThreshold)
        {
            Add("req-pending", Critical, "Requests", $"{requestStats.PendingRequests} requests are stuck pending",
                "Requests are queuing faster than they complete.", "Check the long-running requests table, SQL health and thread pool.");
        }
        else if (requestStats.PendingRequests > 5)
        {
            Add("req-pending", Warning, "Requests", $"{requestStats.PendingRequests} requests pending",
                "More requests than usual are in flight.", "Watch whether this number keeps growing.");
        }

        if (requestStats.LongRunningRequests.Count > 0)
        {
            var longest = requestStats.LongRunningRequests[0];
            Add("req-long", Warning, "Requests",
                $"{requestStats.LongRunningRequests.Count} request(s) running longer than {options.LongRequestThresholdSeconds}s",
                $"Longest: {longest.Method} {longest.Path} for {longest.ElapsedMilliseconds / 1000:F1}s (user {longest.UserName ?? "anonymous"}).",
                "See the long-running requests table; these hold a thread and a DB connection.");
        }

        if (requestStats.TotalRequests >= 20)
        {
            var failRate = (double)requestStats.FailedRequests / requestStats.TotalRequests * 100;
            if (failRate >= 5)
            {
                Add("req-failed", failRate >= 20 ? Critical : Warning, "Requests",
                    $"{failRate:F1}% of HTTP requests failed (5xx)",
                    $"{requestStats.FailedRequests} of {requestStats.TotalRequests}. Last error: {requestStats.LastError ?? "n/a"}",
                    "Check the log file for the exception behind these 500 responses.");
            }
        }

        if (requestStats.CompletedRequests >= 20 && requestStats.AverageResponseTimeMilliseconds >= 1000)
        {
            Add("req-avg", Warning, "Requests", "HTTP responses are slow on average",
                $"Average response time {requestStats.AverageResponseTimeMilliseconds:F0} ms.",
                "Compare with the database latency and slow query tables.");
        }

        // ---------- Process / runtime ----------
        if (latest != null)
        {
            var tp = latest.ThreadPool;
            if (tp.AvailableWorkerThreads < options.FreezeDetection.ThreadPoolWorkerThreshold)
            {
                Add("tp-starved", Critical, "Thread Pool", "Thread pool is starved",
                    $"Only {tp.AvailableWorkerThreads} worker threads are free ({tp.WorkerThreadsInUse} in use).",
                    "Blocking calls (.Result/.Wait(), sync DB calls) are holding threads. This freezes the whole site.");
            }
            else if (tp.AvailableWorkerThreads < 15)
            {
                Add("tp-low", Warning, "Thread Pool", "Thread pool free workers are low",
                    $"{tp.AvailableWorkerThreads} worker threads free ({tp.WorkerThreadsInUse} in use).",
                    "Look for blocking work inside request or hub handlers.");
            }

            var queued = ThreadPool.PendingWorkItemCount;
            if (queued > 200)
            {
                Add("tp-queue", queued > 1000 ? Critical : Warning, "Thread Pool", "Thread pool queue is backed up",
                    $"{queued} work items are waiting for a thread.", "The app cannot keep up with the work being queued.");
            }

            var cpuWindow = recent.TakeLast(6).ToList();
            if (cpuWindow.Count >= 3)
            {
                var avgCpu = cpuWindow.Average(s => s.CpuUsagePercent);
                if (avgCpu >= 85)
                    Add("cpu", Critical, "CPU", "CPU is saturated", $"Average {avgCpu:F0}% over the last {cpuWindow.Count} samples.", "Find the hot path (large queries, logging, serialization).");
                else if (avgCpu >= 70)
                    Add("cpu", Warning, "CPU", "CPU usage is high", $"Average {avgCpu:F0}% over the last {cpuWindow.Count} samples.", "Keep an eye on it; this will worsen with more users.");
            }

            const long gb = 1024L * 1024 * 1024;
            if (latest.WorkingSetBytes > 2 * gb)
            {
                Add("mem-high", Warning, "Memory", "Process memory is high",
                    $"Working set {latest.WorkingSetBytes / (1024 * 1024)} MB (managed {latest.ManagedMemoryBytes / (1024 * 1024)} MB).",
                    "Restart during a quiet window if it keeps rising; look for large in-memory collections.");
            }

            if (recent.Count >= 20)
            {
                var first = recent[0];
                var growthMb = (latest.WorkingSetBytes - first.WorkingSetBytes) / (1024.0 * 1024);
                if (growthMb > 200 && latest.WorkingSetBytes > first.WorkingSetBytes * 1.25)
                {
                    Add("mem-growth", Warning, "Memory", "Memory is growing quickly",
                        $"+{growthMb:F0} MB over the last {(latest.TimestampUtc - first.TimestampUtc).TotalMinutes:F0} minutes.",
                        "Possible leak (unbounded caches, event handlers, undisposed objects).");
                }

                var gen2 = latest.Gen2Collections - first.Gen2Collections;
                if (gen2 >= 10)
                {
                    Add("gc-gen2", Warning, "Memory", "Frequent full garbage collections",
                        $"{gen2} gen-2 collections in the last {(latest.TimestampUtc - first.TimestampUtc).TotalMinutes:F0} minutes.",
                        "Memory pressure is causing pauses; reduce allocations or large object use.");
                }
            }

            if (latest.ThreadCount > 300)
            {
                Add("threads", Warning, "Process", "Very high thread count",
                    $"{latest.ThreadCount} threads.", "Threads are leaking or being created per request.");
            }

            if (latest.HandleCount > 10000)
            {
                Add("handles", Warning, "Process", "Very high handle count",
                    $"{latest.HandleCount} handles.", "Possible handle leak (sockets, files, DB connections).");
            }
        }

        // ---------- Browsers ----------
        var reports = _hub.GetClientReports(TimeSpan.FromMinutes(10));
        var troubled = reports.Where(r => r.HeartbeatFailures > 0 || !r.IsOnline ||
                                          (!string.IsNullOrEmpty(r.SignalRState) && !r.SignalRState.Equals("Connected", StringComparison.OrdinalIgnoreCase))).ToList();
        if (troubled.Count > 0)
        {
            Add("client-problems", troubled.Count >= 5 ? Critical : Warning, "Browsers",
                $"{troubled.Count} browser(s) report connection problems",
                string.Join("; ", troubled.Take(3).Select(r => $"{r.User ?? "anon"}: SignalR {r.SignalRState}, heartbeat failures {r.HeartbeatFailures}")),
                "See the 'Browser reports' table. Many at once usually points to the server; one usually points to that user's network.");
        }

        return issues
            .OrderBy(i => i.Severity == Critical ? 0 : i.Severity == Warning ? 1 : 2)
            .ThenBy(i => i.Category)
            .ToList();
    }

    private void AddHistory(IssueHistoryEntry entry)
    {
        _history.Add(entry);
        if (_history.Count > MaxHistory)
            _history.RemoveAt(0);
    }

    private static string Truncate(string text, int length) =>
        text.Length <= length ? text : text[..length] + "...";
}
