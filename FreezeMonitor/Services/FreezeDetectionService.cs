using Microsoft.Extensions.Options;
using SignalRMVC.FreezeMonitor.Configuration;
using SignalRMVC.FreezeMonitor.Models;

namespace SignalRMVC.FreezeMonitor.Services;

public sealed class FreezeDetectedEventArgs : EventArgs
{
    public string IncidentId { get; }
    public string Reason { get; }
    public HealthSnapshot Snapshot { get; }
    public HealthStatusLevel StatusLevel { get; }

    public FreezeDetectedEventArgs(string incidentId, string reason, HealthSnapshot snapshot, HealthStatusLevel statusLevel)
    {
        IncidentId = incidentId;
        Reason = reason;
        Snapshot = snapshot;
        StatusLevel = statusLevel;
    }
}

public sealed class FreezeDetectionService
{
    private readonly RequestStatisticsService _requestStatistics;
    private readonly SignalRHealthService _signalRHealth;
    private readonly IOptionsMonitor<FreezeMonitorOptions> _options;
    private readonly ILogger _logger;

    private int _consecutiveSqlFailures;
    private int _consecutiveHeartbeatFailures;
    private DateTimeOffset _lastCompletedRequestTimeUtc = DateTimeOffset.UtcNow;
    private long _lastCompletedRequestCount;
    private DateTimeOffset _lastFreezeTriggeredTimeUtc = DateTimeOffset.MinValue;
    private HealthStatusLevel _currentStatusLevel = HealthStatusLevel.Healthy;

    public event Func<FreezeDetectedEventArgs, Task>? FreezeDetected;

    public FreezeDetectionService(
        RequestStatisticsService requestStatistics,
        SignalRHealthService signalRHealth,
        IOptionsMonitor<FreezeMonitorOptions> options,
        ILoggerFactory loggerFactory)
    {
        _requestStatistics = requestStatistics;
        _signalRHealth = signalRHealth;
        _options = options;
        _logger = loggerFactory.CreateLogger("FreezeMonitor");
    }

    public HealthStatusLevel CurrentStatusLevel => _currentStatusLevel;
    public int ConsecutiveSqlFailures => Volatile.Read(ref _consecutiveSqlFailures);
    public int ConsecutiveHeartbeatFailures => Volatile.Read(ref _consecutiveHeartbeatFailures);

    public void RecordHeartbeatSuccess()
    {
        Interlocked.Exchange(ref _consecutiveHeartbeatFailures, 0);
    }

    public void RecordHeartbeatFailure()
    {
        Interlocked.Increment(ref _consecutiveHeartbeatFailures);
    }

    public async Task<HealthStatusLevel> EvaluateAsync(HealthSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        var options = _options.CurrentValue.FreezeDetection;
        if (!options.Enabled)
        {
            _currentStatusLevel = HealthStatusLevel.Healthy;
            return HealthStatusLevel.Healthy;
        }

        var reasons = new List<string>();

        // 1. Check SQL Health failures
        if (!snapshot.Sql.IsHealthy)
        {
            Interlocked.Increment(ref _consecutiveSqlFailures);
        }
        else
        {
            Interlocked.Exchange(ref _consecutiveSqlFailures, 0);
        }

        if (_consecutiveSqlFailures >= options.SqlFailureThreshold)
        {
            reasons.Add($"SQL health check failed {_consecutiveSqlFailures} consecutive times (Threshold: {options.SqlFailureThreshold}).");
        }

        // 2. Check Pending Requests threshold
        if (snapshot.PendingRequests >= options.PendingRequestThreshold)
        {
            reasons.Add($"Pending requests ({snapshot.PendingRequests}) exceeded threshold ({options.PendingRequestThreshold}).");
        }

        // 3. Check ThreadPool worker threads critical level
        if (snapshot.ThreadPool.AvailableWorkerThreads < options.ThreadPoolWorkerThreshold)
        {
            reasons.Add($"ThreadPool available worker threads ({snapshot.ThreadPool.AvailableWorkerThreads}) critically low below threshold ({options.ThreadPoolWorkerThreshold}).");
        }

        // 4. Check Browser/Endpoint Heartbeat failures
        if (_consecutiveHeartbeatFailures >= options.HeartbeatFailureThreshold)
        {
            reasons.Add($"Diagnostics heartbeat failed {_consecutiveHeartbeatFailures} consecutive times (Threshold: {options.HeartbeatFailureThreshold}).");
        }

        // 5. Check Request Completion stall
        var stats = _requestStatistics.GetStatistics(TimeSpan.FromSeconds(_options.CurrentValue.LongRequestThresholdSeconds), _options.CurrentValue.MaxLongRequests);
        if (stats.TotalRequests > _lastCompletedRequestCount)
        {
            _lastCompletedRequestCount = stats.TotalRequests;
            _lastCompletedRequestTimeUtc = DateTimeOffset.UtcNow;
        }
        else if (snapshot.PendingRequests > 0)
        {
            var stalledDuration = DateTimeOffset.UtcNow - _lastCompletedRequestTimeUtc;
            if (stalledDuration.TotalSeconds >= options.NoCompletedRequestSeconds)
            {
                reasons.Add($"No requests completed in the last {stalledDuration.TotalSeconds:F0} seconds while {snapshot.PendingRequests} requests are pending (Threshold: {options.NoCompletedRequestSeconds}s).");
            }
        }

        // 6. Check SignalR activity stoppage vs HTTP request activity
        var signalR = _signalRHealth.GetSnapshot();
        if (snapshot.PendingRequests > 5 && signalR.CurrentConnections == 0 && (signalR.ConnectionsCreatedToday > 0 || signalR.PeakConnections > 0))
        {
            reasons.Add("SignalR connections dropped unexpectedly while HTTP requests continue to queue.");
        }

        // Determine health status level
        HealthStatusLevel newLevel;
        if (reasons.Count >= 2 || (reasons.Count >= 1 && _consecutiveSqlFailures >= options.SqlFailureThreshold && snapshot.PendingRequests >= options.PendingRequestThreshold))
        {
            newLevel = HealthStatusLevel.FreezeDetected;
        }
        else if (reasons.Count == 1)
        {
            newLevel = HealthStatusLevel.Critical;
        }
        else if (!snapshot.Sql.IsHealthy || snapshot.PendingRequests > 5 || snapshot.ThreadPool.AvailableWorkerThreads < 15)
        {
            newLevel = HealthStatusLevel.Warning;
        }
        else
        {
            newLevel = HealthStatusLevel.Healthy;
        }

        _currentStatusLevel = newLevel;

        if (newLevel == HealthStatusLevel.FreezeDetected)
        {
            // Cooldown check (prevent repeated triggers within 5 minutes)
            if (DateTimeOffset.UtcNow - _lastFreezeTriggeredTimeUtc > TimeSpan.FromMinutes(5))
            {
                _lastFreezeTriggeredTimeUtc = DateTimeOffset.UtcNow;
                var reasonSummary = string.Join(" | ", reasons);
                var incidentId = $"INC-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}";

                _logger.LogCritical("AUTOMATIC FREEZE DETECTED! Incident ID: {IncidentId}. Reasons: {Reasons}", incidentId, reasonSummary);

                var handler = FreezeDetected;
                if (handler != null)
                {
                    var args = new FreezeDetectedEventArgs(incidentId, reasonSummary, snapshot, newLevel);
                    await handler.Invoke(args);
                }
            }
        }
        else if (newLevel == HealthStatusLevel.Critical)
        {
            _logger.LogWarning("System Health CRITICAL. Reasons: {Reasons}", string.Join(" | ", reasons));
        }

        return newLevel;
    }
}
