using System.Collections.Concurrent;
using SignalRMVC.FreezeMonitor.Models;

namespace SignalRMVC.FreezeMonitor.Services;

public sealed class SignalRHealthService
{
    private readonly ConcurrentDictionary<string, ConnectionEntry> _connections = new();
    private readonly object _dailyCounterLock = new();
    private DateOnly _counterDay = DateOnly.FromDateTime(DateTime.UtcNow);
    private long _connectionsCreatedToday;
    private long _connectionsClosedToday;
    private long _closedConnectionDurationTicks;
    private long _peakConnections;

    public void TrackConnected(string connectionId, string userId)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        EnsureCurrentDay(nowUtc);
        _connections[connectionId] = new ConnectionEntry(userId, nowUtc, nowUtc);

        lock (_dailyCounterLock)
        {
            _connectionsCreatedToday++;
        }

        UpdatePeak(_connections.Count);
    }

    public void TrackDisconnected(string connectionId)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        EnsureCurrentDay(nowUtc);

        if (_connections.TryRemove(connectionId, out var entry))
        {
            lock (_dailyCounterLock)
            {
                _connectionsClosedToday++;
                _closedConnectionDurationTicks += (nowUtc - entry.ConnectedAtUtc).Ticks;
            }
        }
    }

    public void RecordActivity(string connectionId)
    {
        if (_connections.TryGetValue(connectionId, out var entry))
        {
            _connections.TryUpdate(connectionId, entry with { LastActivityUtc = DateTimeOffset.UtcNow }, entry);
        }
    }

    public SignalRSnapshot GetSnapshot()
    {
        var nowUtc = DateTimeOffset.UtcNow;
        EnsureCurrentDay(nowUtc);
        var activeConnections = _connections.Values.ToArray();
        long createdToday;
        long closedToday;
        long closedDurationTicks;

        lock (_dailyCounterLock)
        {
            createdToday = _connectionsCreatedToday;
            closedToday = _connectionsClosedToday;
            closedDurationTicks = _closedConnectionDurationTicks;
        }

        var activeDurationTicks = activeConnections.Sum(connection => (nowUtc - connection.ConnectedAtUtc).Ticks);
        var durationCount = closedToday + activeConnections.Length;
        var averageDuration = durationCount == 0
            ? 0
            : TimeSpan.FromTicks((closedDurationTicks + activeDurationTicks) / durationCount).TotalSeconds;

        return new SignalRSnapshot(
            activeConnections.Length,
            activeConnections.Select(connection => connection.UserId).Distinct(StringComparer.Ordinal).Count(),
            (int)Interlocked.Read(ref _peakConnections),
            createdToday,
            closedToday,
            Math.Round(averageDuration, 2));
    }

    private void EnsureCurrentDay(DateTimeOffset nowUtc)
    {
        var currentDay = DateOnly.FromDateTime(nowUtc.UtcDateTime);
        lock (_dailyCounterLock)
        {
            if (currentDay == _counterDay)
            {
                return;
            }

            _counterDay = currentDay;
            _connectionsCreatedToday = 0;
            _connectionsClosedToday = 0;
            _closedConnectionDurationTicks = 0;
        }
    }

    private void UpdatePeak(int currentConnections)
    {
        while (true)
        {
            var currentPeak = Interlocked.Read(ref _peakConnections);
            if (currentConnections <= currentPeak ||
                Interlocked.CompareExchange(ref _peakConnections, currentConnections, currentPeak) == currentPeak)
            {
                return;
            }
        }
    }

    private sealed record ConnectionEntry(string UserId, DateTimeOffset ConnectedAtUtc, DateTimeOffset LastActivityUtc);
}
