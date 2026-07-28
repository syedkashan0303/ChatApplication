using System.Text.Json;
using Microsoft.Extensions.Options;
using SignalRMVC.FreezeMonitor.Configuration;
using SignalRMVC.FreezeMonitor.Helpers;
using SignalRMVC.FreezeMonitor.Models;

namespace SignalRMVC.FreezeMonitor.Services;

public sealed class IncidentWriterService
{
    private readonly RingBufferService _ringBuffer;
    private readonly RequestStatisticsService _requestStatisticsService;
    private readonly SignalRHealthService _signalRHealthService;
    private readonly SystemHealthCollector _healthCollector;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly IOptionsMonitor<FreezeMonitorOptions> _options;
    private readonly ILogger _logger;
    private IncidentReport? _latestIncident;

    public IncidentWriterService(
        RingBufferService ringBuffer,
        RequestStatisticsService requestStatisticsService,
        SignalRHealthService signalRHealthService,
        SystemHealthCollector healthCollector,
        IHostEnvironment hostEnvironment,
        IOptionsMonitor<FreezeMonitorOptions> options,
        ILoggerFactory loggerFactory)
    {
        _ringBuffer = ringBuffer;
        _requestStatisticsService = requestStatisticsService;
        _signalRHealthService = signalRHealthService;
        _healthCollector = healthCollector;
        _hostEnvironment = hostEnvironment;
        _options = options;
        _logger = loggerFactory.CreateLogger("FreezeMonitor");
    }

    public async Task<IncidentReport> WriteIncidentAsync(string reason, CancellationToken cancellationToken = default)
    {
        var options = _options.CurrentValue;
        var snapshot = _ringBuffer.GetLatest() ?? await _healthCollector.CollectAsync(options, cancellationToken);
        var statistics = _requestStatisticsService.GetStatistics(
            TimeSpan.FromSeconds(Math.Max(1, options.LongRequestThresholdSeconds)),
            options.MaxLongRequests);
        var signalR = _signalRHealthService.GetSnapshot();
        var directory = CreateIncidentDirectory(options.IncidentFolder, snapshot.TimestampUtc);
        var report = new IncidentReport(
            DateTimeOffset.UtcNow,
            reason,
            directory,
            Environment.MachineName,
            snapshot.ProcessId,
            snapshot,
            statistics,
            signalR,
            snapshot.ThreadPool,
            snapshot.Sql);

        try
        {
            await JsonFileHelper.WriteAsync(Path.Combine(directory, "Health.json"), snapshot, cancellationToken);
            await JsonFileHelper.WriteAsync(Path.Combine(directory, "Statistics.json"), statistics, cancellationToken);
            await JsonFileHelper.WriteAsync(Path.Combine(directory, "SignalR.json"), signalR, cancellationToken);
            await JsonFileHelper.WriteAsync(Path.Combine(directory, "ThreadPool.json"), snapshot.ThreadPool, cancellationToken);
            await JsonFileHelper.WriteAsync(Path.Combine(directory, "SQL.json"), snapshot.Sql, cancellationToken);
            await JsonFileHelper.WriteAsync(Path.Combine(directory, "Application.json"), _healthCollector.GetApplicationInformation(), cancellationToken);
            await JsonFileHelper.WriteAsync(Path.Combine(directory, "Summary.json"), report, cancellationToken);
            Volatile.Write(ref _latestIncident, report);
            _logger.LogCritical("Freeze monitor incident written to {IncidentDirectory}. Reason: {Reason}", directory, reason);
            return report;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Freeze monitor incident write failed for {IncidentDirectory}", directory);
            throw;
        }
    }

    public async Task<IncidentReport?> GetLatestIncidentAsync(CancellationToken cancellationToken = default)
    {
        var inMemoryReport = Volatile.Read(ref _latestIncident);
        if (inMemoryReport is not null)
        {
            return inMemoryReport;
        }

        var rootDirectory = GetIncidentRootDirectory(_options.CurrentValue.IncidentFolder);
        if (!Directory.Exists(rootDirectory))
        {
            return null;
        }

        var latestSummary = Directory.EnumerateDirectories(rootDirectory)
            .Select(directory => new FileInfo(Path.Combine(directory, "Summary.json")))
            .Where(file => file.Exists)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault();
        if (latestSummary is null)
        {
            return null;
        }

        await using var stream = latestSummary.OpenRead();
        var report = await JsonSerializer.DeserializeAsync<IncidentReport>(stream, cancellationToken: cancellationToken);
        if (report is not null)
        {
            Volatile.Write(ref _latestIncident, report);
        }

        return report;
    }

    private string CreateIncidentDirectory(string configuredFolder, DateTimeOffset timestampUtc)
    {
        var rootDirectory = GetIncidentRootDirectory(configuredFolder);
        Directory.CreateDirectory(rootDirectory);
        var name = timestampUtc.UtcDateTime.ToString("yyyy-MM-dd_HH-mm-ss");
        var directory = Path.Combine(rootDirectory, name);
        if (Directory.Exists(directory))
        {
            directory = Path.Combine(rootDirectory, $"{name}_{Guid.NewGuid().ToString("N")[..8]}");
        }

        Directory.CreateDirectory(directory);
        return directory;
    }

    private string GetIncidentRootDirectory(string configuredFolder)
    {
        return Path.IsPathRooted(configuredFolder)
            ? Path.GetFullPath(configuredFolder)
            : Path.GetFullPath(Path.Combine(_hostEnvironment.ContentRootPath, configuredFolder));
    }
}
