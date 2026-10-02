namespace SignalRMVC.FreezeMonitor.Services;

public sealed record HubEvent(DateTimeOffset TimestampUtc, string Kind, string Method, string? User, double DurationMs, string? Message);

public sealed record HubMethodStats(
    string Method,
    long Calls,
    long Errors,
    long SlowCalls,
    double AverageMs,
    double MaxMs,
    string? LastError,
    DateTimeOffset? LastErrorUtc,
    DateTimeOffset LastCallUtc);

public sealed record ClientReport(
    DateTimeOffset TimestampUtc,
    string ClientId,
    string? User,
    string Url,
    string SignalRState,
    int HeartbeatFailures,
    int AjaxFailures,
    int Errors,
    bool IsOnline,
    string? LastError);

public sealed record HubDiagnosticsSnapshot(
    double InvocationsPerMinute,
    int ErrorsLast10Min,
    int SlowLast10Min,
    int AbnormalDisconnectsLast10Min,
    int ConnectsLast5Min,
    int AuthFailuresLast10Min,
    IReadOnlyList<HubMethodStats> Methods,
    IReadOnlyList<HubEvent> RecentEvents);

/// <summary>
/// In-memory hub health tracker (per-method timings, errors, disconnects, auth failures).
/// Everything is bounded: counters per hub method plus capped recent-event queues.
/// </summary>
public sealed class HubDiagnosticsService
{
    public const double SlowThresholdMs = 1000;
    private const int MaxEvents = 100;
    private const int MaxClientReports = 50;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    private readonly object _sync = new();
    private readonly Dictionary<string, MethodAccumulator> _methods = new(StringComparer.Ordinal);
    private readonly Queue<HubEvent> _events = new();
    private readonly Queue<DateTimeOffset> _invocationTimes = new();
    private readonly Queue<DateTimeOffset> _errorTimes = new();
    private readonly Queue<DateTimeOffset> _slowTimes = new();
    private readonly Queue<DateTimeOffset> _abnormalDisconnectTimes = new();
    private readonly Queue<DateTimeOffset> _connectTimes = new();
    private readonly Queue<DateTimeOffset> _authFailureTimes = new();
    private readonly List<ClientReport> _clientReports = new();

    public void RecordInvocation(string method, string? user, double durationMs, Exception? exception)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_sync)
        {
            var acc = GetMethod(method);
            acc.Calls++;
            acc.TotalMs += durationMs;
            acc.MaxMs = Math.Max(acc.MaxMs, durationMs);
            acc.LastCallUtc = now;
            _invocationTimes.Enqueue(now);

            if (exception != null)
            {
                acc.Errors++;
                acc.LastError = Trim(exception.Message);
                acc.LastErrorUtc = now;
                _errorTimes.Enqueue(now);
                AddEvent(new HubEvent(now, "error", method, user, durationMs, Trim(exception.Message)));
            }
            else if (durationMs >= SlowThresholdMs)
            {
                acc.SlowCalls++;
                _slowTimes.Enqueue(now);
                AddEvent(new HubEvent(now, "slow", method, user, durationMs, null));
            }

            TrimAll(now);
        }
    }

    // Hub methods that catch their own exceptions (so the hub filter sees a "success") report here.
    public void RecordHandledError(string method, string? user, string? message)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_sync)
        {
            var acc = GetMethod(method);
            acc.Errors++;
            acc.LastError = Trim(message);
            acc.LastErrorUtc = now;
            _errorTimes.Enqueue(now);
            AddEvent(new HubEvent(now, "handled-error", method, user, 0, Trim(message)));
            TrimAll(now);
        }
    }

    public void RecordConnect()
    {
        lock (_sync)
        {
            _connectTimes.Enqueue(DateTimeOffset.UtcNow);
        }
    }

    public void RecordDisconnect(string? user, Exception? exception)
    {
        if (exception == null) return;

        var now = DateTimeOffset.UtcNow;
        lock (_sync)
        {
            _abnormalDisconnectTimes.Enqueue(now);
            AddEvent(new HubEvent(now, "disconnect", "OnDisconnected", user, 0, Trim(exception.Message)));
            TrimAll(now);
        }
    }

    public void RecordAuthFailure(string? detail)
    {
        var now = DateTimeOffset.UtcNow;
        lock (_sync)
        {
            _authFailureTimes.Enqueue(now);
            AddEvent(new HubEvent(now, "auth", "Authentication", null, 0, Trim(detail)));
            TrimAll(now);
        }
    }

    public void RecordClientReport(ClientReport report)
    {
        lock (_sync)
        {
            // keep only the newest report per browser tab
            _clientReports.RemoveAll(r => r.ClientId == report.ClientId);
            _clientReports.Add(report);
            if (_clientReports.Count > MaxClientReports)
                _clientReports.RemoveAt(0);
        }
    }

    public IReadOnlyList<ClientReport> GetClientReports(TimeSpan maxAge)
    {
        var cutoff = DateTimeOffset.UtcNow - maxAge;
        lock (_sync)
        {
            return _clientReports.Where(r => r.TimestampUtc >= cutoff).OrderByDescending(r => r.TimestampUtc).ToList();
        }
    }

    public HubDiagnosticsSnapshot GetSnapshot()
    {
        var now = DateTimeOffset.UtcNow;
        lock (_sync)
        {
            TrimAll(now);

            var methods = _methods
                .Select(kv => new HubMethodStats(
                    kv.Key,
                    kv.Value.Calls,
                    kv.Value.Errors,
                    kv.Value.SlowCalls,
                    kv.Value.Calls == 0 ? 0 : Math.Round(kv.Value.TotalMs / kv.Value.Calls, 1),
                    Math.Round(kv.Value.MaxMs, 1),
                    kv.Value.LastError,
                    kv.Value.LastErrorUtc,
                    kv.Value.LastCallUtc))
                .OrderByDescending(m => m.Errors)
                .ThenByDescending(m => m.AverageMs)
                .ToList();

            var fiveMinutesAgo = now.AddMinutes(-5);

            return new HubDiagnosticsSnapshot(
                _invocationTimes.Count(t => t >= now.AddMinutes(-1)),
                _errorTimes.Count,
                _slowTimes.Count,
                _abnormalDisconnectTimes.Count,
                _connectTimes.Count(t => t >= fiveMinutesAgo),
                _authFailureTimes.Count,
                methods,
                _events.Reverse().ToList());
        }
    }

    private MethodAccumulator GetMethod(string method)
    {
        if (!_methods.TryGetValue(method, out var acc))
        {
            acc = new MethodAccumulator();
            _methods[method] = acc;
        }
        return acc;
    }

    private void AddEvent(HubEvent hubEvent)
    {
        _events.Enqueue(hubEvent);
        while (_events.Count > MaxEvents) _events.Dequeue();
    }

    private void TrimAll(DateTimeOffset now)
    {
        var cutoff = now - Window;
        TrimQueue(_invocationTimes, now.AddMinutes(-1));
        TrimQueue(_errorTimes, cutoff);
        TrimQueue(_slowTimes, cutoff);
        TrimQueue(_abnormalDisconnectTimes, cutoff);
        TrimQueue(_connectTimes, cutoff);
        TrimQueue(_authFailureTimes, cutoff);
    }

    private static void TrimQueue(Queue<DateTimeOffset> queue, DateTimeOffset cutoff)
    {
        while (queue.Count > 0 && queue.Peek() < cutoff) queue.Dequeue();
    }

    private static string? Trim(string? text) =>
        text is { Length: > 300 } ? text[..300] + "..." : text;

    private sealed class MethodAccumulator
    {
        public long Calls;
        public long Errors;
        public long SlowCalls;
        public double TotalMs;
        public double MaxMs;
        public string? LastError;
        public DateTimeOffset? LastErrorUtc;
        public DateTimeOffset LastCallUtc;
    }
}
