using System.Collections.Concurrent;
using System.Diagnostics;
using SignalRMVC.FreezeMonitor.Models;

namespace SignalRMVC.FreezeMonitor.Services;

public sealed class RequestStatisticsService
{
    private readonly ConcurrentDictionary<string, RequestEntry> _activeRequests = new();
    private readonly ConcurrentQueue<DateTimeOffset> _completedRequestTimes = new();
    private long _completedRequests;
    private long _failedRequests;
    private long _totalRequests;
    private long _totalElapsedTicks;
    private long _longestElapsedTicks;
    private string? _lastError;

    public void StartRequest(string requestId, string method, string path, string? userName, string connectionId)
    {
        var entry = new RequestEntry(
            requestId,
            method,
            path,
            userName,
            connectionId,
            Environment.CurrentManagedThreadId,
            DateTimeOffset.UtcNow,
            Stopwatch.GetTimestamp());

        _activeRequests[requestId] = entry;
        Interlocked.Increment(ref _totalRequests);
    }

    public double CompleteRequest(string requestId, int statusCode, Exception? exception = null)
    {
        if (!_activeRequests.TryRemove(requestId, out var entry))
        {
            return 0;
        }

        var elapsed = Stopwatch.GetElapsedTime(entry.StartTimestamp);
        Interlocked.Increment(ref _completedRequests);
        Interlocked.Add(ref _totalElapsedTicks, elapsed.Ticks);
        UpdateLongest(elapsed.Ticks);
        _completedRequestTimes.Enqueue(DateTimeOffset.UtcNow);

        if (exception is not null || statusCode >= StatusCodes.Status500InternalServerError)
        {
            Interlocked.Increment(ref _failedRequests);
            Volatile.Write(ref _lastError, exception?.Message ?? $"HTTP {statusCode} for {entry.Method} {entry.Path}");
        }

        return elapsed.TotalMilliseconds;
    }

    public RequestStatistics GetStatistics(TimeSpan longRequestThreshold, int maximumLongRequests)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        TrimCompletedRequestWindow(nowUtc);

        var completed = Interlocked.Read(ref _completedRequests);
        var totalElapsedTicks = Interlocked.Read(ref _totalElapsedTicks);
        var longRequests = GetLongRunningRequests(longRequestThreshold, maximumLongRequests, nowUtc);

        return new RequestStatistics(
            _activeRequests.Count,
            _activeRequests.Count,
            completed,
            Interlocked.Read(ref _failedRequests),
            Interlocked.Read(ref _totalRequests),
            TimeSpan.FromTicks(Interlocked.Read(ref _longestElapsedTicks)).TotalMilliseconds,
            completed == 0 ? 0 : TimeSpan.FromTicks(totalElapsedTicks / completed).TotalMilliseconds,
            _completedRequestTimes.Count,
            Volatile.Read(ref _lastError),
            longRequests);
    }

    private IReadOnlyList<LongRunningRequest> GetLongRunningRequests(
        TimeSpan threshold,
        int maximumLongRequests,
        DateTimeOffset nowUtc)
    {
        return _activeRequests.Values
            .Select(entry => new LongRunningRequest(
                entry.RequestId,
                entry.Method,
                entry.Path,
                entry.UserName,
                entry.ConnectionId,
                entry.ThreadId,
                entry.StartedAtUtc,
                Stopwatch.GetElapsedTime(entry.StartTimestamp).TotalMilliseconds))
            .Where(request => request.ElapsedMilliseconds >= threshold.TotalMilliseconds)
            .OrderByDescending(request => request.ElapsedMilliseconds)
            .Take(Math.Max(maximumLongRequests, 0))
            .ToArray();
    }

    private void TrimCompletedRequestWindow(DateTimeOffset nowUtc)
    {
        var cutoff = nowUtc.AddMinutes(-1);
        while (_completedRequestTimes.TryPeek(out var completedAt) && completedAt < cutoff)
        {
            _completedRequestTimes.TryDequeue(out _);
        }
    }

    private void UpdateLongest(long elapsedTicks)
    {
        while (true)
        {
            var current = Interlocked.Read(ref _longestElapsedTicks);
            if (elapsedTicks <= current || Interlocked.CompareExchange(ref _longestElapsedTicks, elapsedTicks, current) == current)
            {
                return;
            }
        }
    }

    private sealed record RequestEntry(
        string RequestId,
        string Method,
        string Path,
        string? UserName,
        string ConnectionId,
        int ThreadId,
        DateTimeOffset StartedAtUtc,
        long StartTimestamp);
}
