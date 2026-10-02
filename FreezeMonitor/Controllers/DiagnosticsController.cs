using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SignalRMVC.FreezeMonitor.Configuration;
using SignalRMVC.FreezeMonitor.Models;
using SignalRMVC.FreezeMonitor.Services;

namespace SignalRMVC.FreezeMonitor.Controllers;

[ApiController]
[Authorize(Roles = "Manager")]
[Route("diagnostics")]
public sealed class DiagnosticsController : ControllerBase
{
    private readonly RingBufferService _ringBuffer;
    private readonly RequestStatisticsService _requestStatisticsService;
    private readonly SignalRHealthService _signalRHealthService;
    private readonly IncidentWriterService _incidentWriterService;
    private readonly SystemHealthCollector _healthCollector;
    private readonly FreezeDetectionService _freezeDetectionService;
    private readonly DiagnosticsAnalyzer _analyzer;
    private readonly HubDiagnosticsService _hubDiagnostics;
    private readonly DbDiagnosticsService _dbDiagnostics;
    private readonly IOptionsMonitor<FreezeMonitorOptions> _options;
    private readonly ILogger<DiagnosticsController> _logger;

    public DiagnosticsController(
        RingBufferService ringBuffer,
        RequestStatisticsService requestStatisticsService,
        SignalRHealthService signalRHealthService,
        IncidentWriterService incidentWriterService,
        SystemHealthCollector healthCollector,
        FreezeDetectionService freezeDetectionService,
        DiagnosticsAnalyzer analyzer,
        HubDiagnosticsService hubDiagnostics,
        DbDiagnosticsService dbDiagnostics,
        IOptionsMonitor<FreezeMonitorOptions> options,
        ILogger<DiagnosticsController> logger)
    {
        _ringBuffer = ringBuffer;
        _requestStatisticsService = requestStatisticsService;
        _signalRHealthService = signalRHealthService;
        _incidentWriterService = incidentWriterService;
        _healthCollector = healthCollector;
        _freezeDetectionService = freezeDetectionService;
        _analyzer = analyzer;
        _hubDiagnostics = hubDiagnostics;
        _dbDiagnostics = dbDiagnostics;
        _options = options;
        _logger = logger;
    }

    [HttpGet("health")]
    public IActionResult GetHealth()
    {
        if (!DiagnosticsEnabled())
        {
            return NotFound();
        }

        var options = _options.CurrentValue;
        var statistics = _requestStatisticsService.GetStatistics(
            TimeSpan.FromSeconds(Math.Max(1, options.LongRequestThresholdSeconds)),
            options.MaxLongRequests);

        return Ok(new
        {
            statusLevel = _freezeDetectionService.CurrentStatusLevel.ToString(),
            application = _healthCollector.GetApplicationInformation(),
            environment = _healthCollector.GetEnvironmentInformation(),
            health = _ringBuffer.GetLatest(),
            statistics,
            signalR = _signalRHealthService.GetSnapshot()
        });
    }

    // One call for the whole Diagnostics page: detected issues + hub / database / browser detail.
    [HttpGet("overview")]
    public IActionResult GetOverview()
    {
        if (!DiagnosticsEnabled())
        {
            return NotFound();
        }

        var nowUtc = DateTimeOffset.UtcNow;
        var latest = _ringBuffer.GetLatest();

        return Ok(new
        {
            generatedAtUtc = nowUtc,
            monitorLagSeconds = latest == null ? (double?)null : Math.Round((nowUtc - latest.TimestampUtc).TotalSeconds, 1),
            issues = _analyzer.GetCurrentIssues(),
            issueHistory = _analyzer.GetHistory(),
            hub = _hubDiagnostics.GetSnapshot(),
            database = _dbDiagnostics.GetSnapshot(),
            clientReports = _hubDiagnostics.GetClientReports(TimeSpan.FromMinutes(10)),
            runtime = new
            {
                threadPoolPendingWorkItems = ThreadPool.PendingWorkItemCount,
                threadPoolCompletedWorkItems = ThreadPool.CompletedWorkItemCount,
                lockContentions = System.Threading.Monitor.LockContentionCount
            }
        });
    }

    [HttpGet("statistics")]
    public IActionResult GetStatistics()
    {
        if (!DiagnosticsEnabled())
        {
            return NotFound();
        }

        var options = _options.CurrentValue;
        return Ok(_requestStatisticsService.GetStatistics(
            TimeSpan.FromSeconds(Math.Max(1, options.LongRequestThresholdSeconds)),
            options.MaxLongRequests));
    }

    [HttpGet("ringbuffer")]
    public IActionResult GetRingBuffer()
    {
        if (!DiagnosticsEnabled())
        {
            return NotFound();
        }

        return Ok(_ringBuffer.GetLatest(50));
    }

    [HttpGet("incidents")]
    public async Task<IActionResult> GetIncidents(CancellationToken cancellationToken)
    {
        if (!DiagnosticsEnabled())
        {
            return NotFound();
        }

        var incidents = await _incidentWriterService.GetAllIncidentsAsync(cancellationToken);
        return Ok(incidents);
    }

    [HttpGet("incidents/{id}")]
    public async Task<IActionResult> GetIncidentById(string id, CancellationToken cancellationToken)
    {
        if (!DiagnosticsEnabled())
        {
            return NotFound();
        }

        var incident = await _incidentWriterService.GetIncidentByIdAsync(id, cancellationToken);
        return incident is null ? NotFound() : Ok(incident);
    }

    [HttpGet("incident/latest")]
    public async Task<IActionResult> GetLatestIncident(CancellationToken cancellationToken)
    {
        if (!DiagnosticsEnabled())
        {
            return NotFound();
        }

        var latest = await _incidentWriterService.GetLatestIncidentAsync(cancellationToken);
        return latest is null ? NotFound() : Ok(latest);
    }

    [HttpPost("incident/create")]
    public async Task<IActionResult> CreateManualIncident([FromQuery] string? reason, CancellationToken cancellationToken)
    {
        if (!DiagnosticsEnabled())
        {
            return NotFound();
        }

        var actualReason = string.IsNullOrWhiteSpace(reason) ? "Manual incident capture triggered by Administrator" : reason;
        var report = await _incidentWriterService.WriteIncidentAsync(actualReason, HealthStatusLevel.Warning, cancellationToken: cancellationToken);

        _logger.LogInformation("Manual incident capture triggered successfully by user {User}. Incident ID: {IncidentId}", User.Identity?.Name, report.IncidentId);
        return Ok(report);
    }

    private bool DiagnosticsEnabled()
    {
        var options = _options.CurrentValue;
        return options.Enabled && options.EnableDiagnosticsApi;
    }
}
