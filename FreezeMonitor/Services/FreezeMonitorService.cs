using Microsoft.Extensions.Options;
using SignalRMVC.FreezeMonitor.Configuration;

namespace SignalRMVC.FreezeMonitor.Services;

public sealed class FreezeMonitorService : BackgroundService
{
    private readonly SystemHealthCollector _healthCollector;
    private readonly SqlHealthChecker _sqlHealthChecker;
    private readonly RingBufferService _ringBuffer;
    private readonly IOptionsMonitor<FreezeMonitorOptions> _options;
    private readonly ILogger _logger;

    public FreezeMonitorService(
        SystemHealthCollector healthCollector,
        SqlHealthChecker sqlHealthChecker,
        RingBufferService ringBuffer,
        IOptionsMonitor<FreezeMonitorOptions> options,
        ILoggerFactory loggerFactory)
    {
        _healthCollector = healthCollector;
        _sqlHealthChecker = sqlHealthChecker;
        _ringBuffer = ringBuffer;
        _options = options;
        _logger = loggerFactory.CreateLogger("FreezeMonitor");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = _options.CurrentValue;
        var samplingInterval = TimeSpan.FromSeconds(Math.Max(1, options.SamplingIntervalSeconds));
        var nextSqlCheckUtc = DateTimeOffset.MinValue;

        _logger.LogInformation(
            "Freeze monitor started with {SamplingIntervalSeconds}s sampling, {SqlHealthIntervalSeconds}s SQL checks, and ring buffer capacity {RingBufferCapacity}",
            samplingInterval.TotalSeconds,
            options.SqlHealthIntervalSeconds,
            _ringBuffer.Capacity);

        using var timer = new PeriodicTimer(samplingInterval);

        do
        {
            try
            {
                options = _options.CurrentValue;
                var nowUtc = DateTimeOffset.UtcNow;
                if (nowUtc >= nextSqlCheckUtc)
                {
                    await _sqlHealthChecker.CheckAsync(stoppingToken);
                    nextSqlCheckUtc = nowUtc.AddSeconds(Math.Max(1, options.SqlHealthIntervalSeconds));
                }

                var snapshot = await _healthCollector.CollectAsync(options, stoppingToken);
                _ringBuffer.Add(snapshot);
                _logger.LogInformation(
                    "Health snapshot collected: CPU={CpuUsagePercent}%, WorkingSet={WorkingSetBytes}, PendingRequests={PendingRequests}, SignalRConnections={SignalRConnections}, SqlHealthy={SqlHealthy}",
                    snapshot.CpuUsagePercent,
                    snapshot.WorkingSetBytes,
                    snapshot.PendingRequests,
                    snapshot.ActiveSignalRConnections,
                    snapshot.Sql.IsHealthy);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Freeze monitor sampling failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));

        _logger.LogInformation("Freeze monitor stopped");
    }
}
