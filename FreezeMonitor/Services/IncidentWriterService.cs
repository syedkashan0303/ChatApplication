using System.Text.Json;
using Microsoft.Extensions.Options;
using SignalRMVC.FreezeMonitor.Configuration;
using SignalRMVC.FreezeMonitor.Helpers;
using SignalRMVC.FreezeMonitor.Logging;
using SignalRMVC.FreezeMonitor.Models;

namespace SignalRMVC.FreezeMonitor.Services;

public sealed class IncidentWriterService
{
    private readonly RingBufferService _ringBuffer;
    private readonly RequestStatisticsService _requestStatisticsService;
    private readonly SignalRHealthService _signalRHealthService;
    private readonly SystemHealthCollector _healthCollector;
    private readonly ProcDumpService _procDumpService;
    private readonly EmailNotificationService _emailNotificationService;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly IOptionsMonitor<FreezeMonitorOptions> _options;
    private readonly ILogger _logger;
    private IncidentReport? _latestIncident;

    public IncidentWriterService(
        RingBufferService ringBuffer,
        RequestStatisticsService requestStatisticsService,
        SignalRHealthService signalRHealthService,
        SystemHealthCollector healthCollector,
        ProcDumpService procDumpService,
        EmailNotificationService emailNotificationService,
        IHostEnvironment hostEnvironment,
        IOptionsMonitor<FreezeMonitorOptions> options,
        ILoggerFactory loggerFactory)
    {
        _ringBuffer = ringBuffer;
        _requestStatisticsService = requestStatisticsService;
        _signalRHealthService = signalRHealthService;
        _healthCollector = healthCollector;
        _procDumpService = procDumpService;
        _emailNotificationService = emailNotificationService;
        _hostEnvironment = hostEnvironment;
        _options = options;
        _logger = loggerFactory.CreateLogger("FreezeMonitor");
    }

    public async Task<IncidentReport> WriteIncidentAsync(
        string reason,
        HealthStatusLevel statusLevel = HealthStatusLevel.FreezeDetected,
        string? customIncidentId = null,
        CancellationToken cancellationToken = default)
    {
        var options = _options.CurrentValue;
        var nowUtc = DateTimeOffset.UtcNow;
        var incidentId = customIncidentId ?? $"INC-{nowUtc:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}";

        FreezeMonitorLogEnricher.SetCurrentIncidentContext(incidentId);

        var snapshot = _ringBuffer.GetLatest() ?? await _healthCollector.CollectAsync(options, cancellationToken);
        var statistics = _requestStatisticsService.GetStatistics(
            TimeSpan.FromSeconds(Math.Max(1, options.LongRequestThresholdSeconds)),
            options.MaxLongRequests);
        var signalR = _signalRHealthService.GetSnapshot();
        var envInfo = _healthCollector.GetEnvironmentInformation();
        var directory = CreateIncidentDirectory(options.IncidentFolder, nowUtc);

        var capturedFiles = new List<string>
        {
            "Health.json",
            "Statistics.json",
            "SignalR.json",
            "ThreadPool.json",
            "SQL.json",
            "Application.json",
            "Environment.json",
            "Summary.json"
        };

        var report = new IncidentReport(
            incidentId,
            nowUtc,
            reason,
            directory,
            Environment.MachineName,
            snapshot.ProcessId,
            envInfo,
            snapshot,
            statistics,
            signalR,
            snapshot.ThreadPool,
            snapshot.Sql,
            statusLevel,
            capturedFiles);

        try
        {
            await JsonFileHelper.WriteAsync(Path.Combine(directory, "Health.json"), snapshot, cancellationToken);
            await JsonFileHelper.WriteAsync(Path.Combine(directory, "Statistics.json"), statistics, cancellationToken);
            await JsonFileHelper.WriteAsync(Path.Combine(directory, "SignalR.json"), signalR, cancellationToken);
            await JsonFileHelper.WriteAsync(Path.Combine(directory, "ThreadPool.json"), snapshot.ThreadPool, cancellationToken);
            await JsonFileHelper.WriteAsync(Path.Combine(directory, "SQL.json"), snapshot.Sql, cancellationToken);
            await JsonFileHelper.WriteAsync(Path.Combine(directory, "Application.json"), _healthCollector.GetApplicationInformation(), cancellationToken);
            await JsonFileHelper.WriteAsync(Path.Combine(directory, "Environment.json"), envInfo, cancellationToken);
            await JsonFileHelper.WriteAsync(Path.Combine(directory, "Summary.json"), report, cancellationToken);

            Volatile.Write(ref _latestIncident, report);
            _logger.LogCritical("Freeze monitor incident created at {Directory}. Incident ID: {IncidentId}, Reason: {Reason}", directory, incidentId, reason);

            // Execute ProcDump if enabled (isolated fail-safe execution)
            var dumpCaptured = await _procDumpService.ExecuteProcDumpAsync(directory, cancellationToken);
            if (dumpCaptured)
            {
                capturedFiles.Add("dump.dmp");
                // Re-write summary with updated captured files list
                await JsonFileHelper.WriteAsync(Path.Combine(directory, "Summary.json"), report with { CapturedFiles = capturedFiles }, cancellationToken);
            }

            // Send SMTP notification if enabled (isolated fail-safe execution)
            await _emailNotificationService.SendIncidentEmailAsync(report, cancellationToken);

            return report;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Freeze monitor incident write failed for {Directory}", directory);
            throw;
        }
        finally
        {
            FreezeMonitorLogEnricher.ClearCurrentIncidentContext();
        }
    }

    public async Task<IncidentReport?> GetLatestIncidentAsync(CancellationToken cancellationToken = default)
    {
        var inMemoryReport = Volatile.Read(ref _latestIncident);
        if (inMemoryReport is not null)
        {
            return inMemoryReport;
        }

        var incidents = await GetAllIncidentsAsync(cancellationToken);
        if (!incidents.Any())
        {
            return null;
        }

        var latestSummary = incidents.OrderByDescending(i => i.TimestampUtc).First();
        return await GetIncidentByIdAsync(latestSummary.IncidentId, cancellationToken);
    }

    public async Task<IReadOnlyList<IncidentSummaryDto>> GetAllIncidentsAsync(CancellationToken cancellationToken = default)
    {
        var rootDirectory = GetIncidentRootDirectory(_options.CurrentValue.IncidentFolder);
        if (!Directory.Exists(rootDirectory))
        {
            return Array.Empty<IncidentSummaryDto>();
        }

        var list = new List<IncidentSummaryDto>();
        var subdirectories = Directory.EnumerateDirectories(rootDirectory);

        foreach (var subDir in subdirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var summaryFile = Path.Combine(subDir, "Summary.json");
            if (!File.Exists(summaryFile)) continue;

            try
            {
                await using var stream = File.OpenRead(summaryFile);
                var report = await JsonSerializer.DeserializeAsync<IncidentReport>(stream, cancellationToken: cancellationToken);
                if (report != null)
                {
                    var files = Directory.EnumerateFiles(subDir).Select(Path.GetFileName).Where(f => f != null).Cast<string>().ToList();
                    list.Add(new IncidentSummaryDto(
                        report.IncidentId,
                        report.TimestampUtc,
                        report.Reason,
                        report.StatusLevel.ToString(),
                        report.MachineName,
                        report.ProcessId,
                        subDir,
                        files,
                        report.StatusLevel
                    ));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not parse summary file at {SummaryFile}", summaryFile);
            }
        }

        return list.OrderByDescending(x => x.TimestampUtc).ToList();
    }

    public async Task<IncidentReport?> GetIncidentByIdAsync(string incidentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(incidentId)) return null;

        var rootDirectory = GetIncidentRootDirectory(_options.CurrentValue.IncidentFolder);
        if (!Directory.Exists(rootDirectory)) return null;

        var subdirectories = Directory.EnumerateDirectories(rootDirectory);
        foreach (var subDir in subdirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var summaryFile = Path.Combine(subDir, "Summary.json");
            if (!File.Exists(summaryFile)) continue;

            try
            {
                await using var stream = File.OpenRead(summaryFile);
                var report = await JsonSerializer.DeserializeAsync<IncidentReport>(stream, cancellationToken: cancellationToken);
                if (report != null && string.Equals(report.IncidentId, incidentId, StringComparison.OrdinalIgnoreCase))
                {
                    var files = Directory.EnumerateFiles(subDir).Select(Path.GetFileName).Where(f => f != null).Cast<string>().ToList();
                    return report with { CapturedFiles = files };
                }
            }
            catch
            {
                // continue searching
            }
        }

        return null;
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
