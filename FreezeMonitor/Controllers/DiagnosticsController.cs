using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SignalRMVC.FreezeMonitor.Configuration;
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
    private readonly IOptionsMonitor<FreezeMonitorOptions> _options;

    public DiagnosticsController(
        RingBufferService ringBuffer,
        RequestStatisticsService requestStatisticsService,
        SignalRHealthService signalRHealthService,
        IncidentWriterService incidentWriterService,
        SystemHealthCollector healthCollector,
        IOptionsMonitor<FreezeMonitorOptions> options)
    {
        _ringBuffer = ringBuffer;
        _requestStatisticsService = requestStatisticsService;
        _signalRHealthService = signalRHealthService;
        _incidentWriterService = incidentWriterService;
        _healthCollector = healthCollector;
        _options = options;
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
            application = _healthCollector.GetApplicationInformation(),
            health = _ringBuffer.GetLatest(),
            statistics,
            signalR = _signalRHealthService.GetSnapshot()
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

    private bool DiagnosticsEnabled()
    {
        var options = _options.CurrentValue;
        return options.Enabled && options.EnableDiagnosticsApi;
    }
}
