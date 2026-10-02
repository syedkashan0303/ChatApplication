using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace SignalRMVC.FreezeMonitor.Services;

public sealed record DbQueryStat(string Sql, long Calls, double AverageMs, double MaxMs, double TotalMs, long SlowCalls, long Failures, DateTimeOffset LastSeenUtc);

public sealed record DbEvent(DateTimeOffset TimestampUtc, string Kind, string Sql, double DurationMs, string? Message);

public sealed record DbDiagnosticsSnapshot(
    long TotalCommands,
    double CommandsPerMinute,
    int SlowLast10Min,
    int FailedLast10Min,
    IReadOnlyList<DbQueryStat> Queries,
    IReadOnlyList<DbEvent> RecentEvents);

/// <summary>
/// Tracks Entity Framework command timings. Only parameterised command text is kept (never parameter values).
/// </summary>
public sealed class DbDiagnosticsService
{
    public const double SlowThresholdMs = 500;
    private const int MaxTrackedQueries = 300;
    private const int MaxEvents = 100;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    private readonly object _sync = new();
    private readonly Dictionary<string, QueryAccumulator> _queries = new(StringComparer.Ordinal);
    private readonly Queue<DbEvent> _events = new();
    private readonly Queue<DateTimeOffset> _commandTimes = new();
    private readonly Queue<DateTimeOffset> _slowTimes = new();
    private readonly Queue<DateTimeOffset> _failureTimes = new();
    private long _totalCommands;

    public void Record(string commandText, double durationMs, string? error)
    {
        var now = DateTimeOffset.UtcNow;
        var key = Normalize(commandText);

        lock (_sync)
        {
            _totalCommands++;
            _commandTimes.Enqueue(now);

            if (_queries.TryGetValue(key, out var acc) || _queries.Count < MaxTrackedQueries)
            {
                if (acc == null)
                {
                    acc = new QueryAccumulator();
                    _queries[key] = acc;
                }

                acc.Calls++;
                acc.TotalMs += durationMs;
                acc.MaxMs = Math.Max(acc.MaxMs, durationMs);
                acc.LastSeenUtc = now;
                if (error != null) acc.Failures++;
                if (durationMs >= SlowThresholdMs) acc.SlowCalls++;
            }

            if (error != null)
            {
                _failureTimes.Enqueue(now);
                AddEvent(new DbEvent(now, "failed", key, durationMs, error.Length > 300 ? error[..300] + "..." : error));
            }
            else if (durationMs >= SlowThresholdMs)
            {
                _slowTimes.Enqueue(now);
                AddEvent(new DbEvent(now, "slow", key, durationMs, null));
            }

            TrimQueue(_commandTimes, now.AddMinutes(-1));
            TrimQueue(_slowTimes, now - Window);
            TrimQueue(_failureTimes, now - Window);
        }
    }

    public DbDiagnosticsSnapshot GetSnapshot()
    {
        var now = DateTimeOffset.UtcNow;
        lock (_sync)
        {
            TrimQueue(_commandTimes, now.AddMinutes(-1));
            TrimQueue(_slowTimes, now - Window);
            TrimQueue(_failureTimes, now - Window);

            var queries = _queries
                .Select(kv => new DbQueryStat(
                    kv.Key,
                    kv.Value.Calls,
                    Math.Round(kv.Value.TotalMs / Math.Max(1, kv.Value.Calls), 1),
                    Math.Round(kv.Value.MaxMs, 1),
                    Math.Round(kv.Value.TotalMs, 0),
                    kv.Value.SlowCalls,
                    kv.Value.Failures,
                    kv.Value.LastSeenUtc))
                .OrderByDescending(q => q.TotalMs)
                .Take(15)
                .ToList();

            return new DbDiagnosticsSnapshot(
                _totalCommands,
                _commandTimes.Count,
                _slowTimes.Count,
                _failureTimes.Count,
                queries,
                _events.Reverse().ToList());
        }
    }

    private void AddEvent(DbEvent dbEvent)
    {
        _events.Enqueue(dbEvent);
        while (_events.Count > MaxEvents) _events.Dequeue();
    }

    private static void TrimQueue(Queue<DateTimeOffset> queue, DateTimeOffset cutoff)
    {
        while (queue.Count > 0 && queue.Peek() < cutoff) queue.Dequeue();
    }

    private static string Normalize(string commandText)
    {
        var text = Whitespace.Replace(commandText ?? string.Empty, " ").Trim();
        return text.Length > 240 ? text[..240] + "..." : text;
    }

    private sealed class QueryAccumulator
    {
        public long Calls;
        public long Failures;
        public long SlowCalls;
        public double TotalMs;
        public double MaxMs;
        public DateTimeOffset LastSeenUtc;
    }
}

public sealed class DbDiagnosticsInterceptor : DbCommandInterceptor
{
    private readonly DbDiagnosticsService _diagnostics;

    public DbDiagnosticsInterceptor(DbDiagnosticsService diagnostics)
    {
        _diagnostics = diagnostics;
    }

    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
    {
        _diagnostics.Record(command.CommandText, eventData.Duration.TotalMilliseconds, null);
        return result;
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
    {
        _diagnostics.Record(command.CommandText, eventData.Duration.TotalMilliseconds, null);
        return new ValueTask<DbDataReader>(result);
    }

    public override int NonQueryExecuted(DbCommand command, CommandExecutedEventData eventData, int result)
    {
        _diagnostics.Record(command.CommandText, eventData.Duration.TotalMilliseconds, null);
        return result;
    }

    public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        _diagnostics.Record(command.CommandText, eventData.Duration.TotalMilliseconds, null);
        return new ValueTask<int>(result);
    }

    public override object? ScalarExecuted(DbCommand command, CommandExecutedEventData eventData, object? result)
    {
        _diagnostics.Record(command.CommandText, eventData.Duration.TotalMilliseconds, null);
        return result;
    }

    public override ValueTask<object?> ScalarExecutedAsync(DbCommand command, CommandExecutedEventData eventData, object? result, CancellationToken cancellationToken = default)
    {
        _diagnostics.Record(command.CommandText, eventData.Duration.TotalMilliseconds, null);
        return new ValueTask<object?>(result);
    }

    public override void CommandFailed(DbCommand command, CommandErrorEventData eventData)
    {
        _diagnostics.Record(command.CommandText, eventData.Duration.TotalMilliseconds, eventData.Exception.Message);
    }

    public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        _diagnostics.Record(command.CommandText, eventData.Duration.TotalMilliseconds, eventData.Exception.Message);
        return Task.CompletedTask;
    }
}
