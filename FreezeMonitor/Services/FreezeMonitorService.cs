using Microsoft.Extensions.Options;
using SignalRMVC.FreezeMonitor.Configuration;
using SignalRMVC.FreezeMonitor.Models;

namespace SignalRMVC.FreezeMonitor.Services;

public sealed class FreezeMonitorService : BackgroundService
{
    private readonly SystemHealthCollector _healthCollector;
    private readonly SqlHealthChecker _sqlHealthChecker;
    private readonly RingBufferService _ringBuffer;
    private readonly FreezeDetectionService _freezeDetectionService;
    private readonly IncidentWriterService _incidentWriterService;
    private readonly IOptionsMonitor<FreezeMonitorOptions> _options;
    private readonly ILogger _logger;

    public FreezeMonitorService(
        SystemHealthCollector healthCollector,
        SqlHealthChecker sqlHealthChecker,
        RingBufferService ringBuffer,
        FreezeDetectionService freezeDetectionService,
        IncidentWriterService incidentWriterService,
        IOptionsMonitor<FreezeMonitorOptions> options,
        ILoggerFactory loggerFactory)
    {
        _healthCollector = healthCollector;
        _sqlHealthChecker = sqlHealthChecker;
        _ringBuffer = ringBuffer;
        _freezeDetectionService = freezeDetectionService;
        _incidentWriterService = incidentWriterService;
        _options = options;
        _logger = loggerFactory.CreateLogger("FreezeMonitor");

        _freezeDetectionService.FreezeDetected += OnFreezeDetectedAsync;
    }

    private async Task OnFreezeDetectedAsync(FreezeDetectedEventArgs args)
    {
        try
        {
            _logger.LogCritical("Freeze monitor received FreezeDetected event. Writing incident bundle for Incident ID {IncidentId}", args.IncidentId);
            await _incidentWriterService.WriteIncidentAsync(args.Reason, args.StatusLevel, args.IncidentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write incident bundle during FreezeDetected event for Incident ID {IncidentId}", args.IncidentId);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var options = _options.CurrentValue;
        var samplingInterval = TimeSpan.FromSeconds(Math.Max(1, options.SamplingIntervalSeconds));
        var nextSqlCheckUtc = DateTimeOffset.MinValue;

        _logger.LogInformation(
            "Freeze monitor Phase 3 started with {SamplingIntervalSeconds}s sampling, {SqlHealthIntervalSeconds}s SQL checks, and ring buffer capacity {RingBufferCapacity}",
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

                // Evaluate multi-condition freeze rules
                var statusLevel = await _freezeDetectionService.EvaluateAsync(snapshot, stoppingToken);

                _logger.LogInformation(
                    "Health snapshot collected: Level={StatusLevel}, CPU={CpuUsagePercent}%, WorkingSet={WorkingSetBytes}, PendingRequests={PendingRequests}, SignalRConnections={SignalRConnections}, SqlHealthy={SqlHealthy}",
                    statusLevel,
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
