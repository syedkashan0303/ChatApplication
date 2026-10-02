using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SignalRMVC.FreezeMonitor.Configuration;
using SignalRMVC.FreezeMonitor.Models;
using SignalRMVC.FreezeMonitor.Services;

namespace SignalRMVC.FreezeMonitor.Controllers;

/// <summary>
/// Write-only endpoints called by freeze-monitor.js from every logged-in user's browser.
/// They expose no data; everything that shows diagnostics lives in DiagnosticsController (Manager only).
/// </summary>
[ApiController]
[Authorize]
[Route("diagnostics")]
public sealed class DiagnosticsClientController : ControllerBase
{
    private readonly FreezeDetectionService _freezeDetectionService;
    private readonly HubDiagnosticsService _hubDiagnostics;
    private readonly IOptionsMonitor<FreezeMonitorOptions> _options;
    private readonly ILogger<DiagnosticsClientController> _logger;

    public DiagnosticsClientController(
        FreezeDetectionService freezeDetectionService,
        HubDiagnosticsService hubDiagnostics,
        IOptionsMonitor<FreezeMonitorOptions> options,
        ILogger<DiagnosticsClientController> logger)
    {
        _freezeDetectionService = freezeDetectionService;
        _hubDiagnostics = hubDiagnostics;
        _options = options;
        _logger = logger;
    }

    [HttpGet("heartbeat")]
    public IActionResult Heartbeat()
    {
        if (!DiagnosticsEnabled())
        {
            return NotFound();
        }

        _freezeDetectionService.RecordHeartbeatSuccess();
        return Ok(new { status = "healthy", timestamp = DateTimeOffset.UtcNow });
    }

    [HttpPost("browser")]
    public IActionResult PostBrowserDiagnostics([FromBody] BrowserDiagnosticsPayload payload)
    {
        if (!DiagnosticsEnabled())
        {
            return NotFound();
        }

        if (payload.ConsecutiveHeartbeatFailures > 0)
        {
            _freezeDetectionService.RecordHeartbeatFailure();
        }

        _hubDiagnostics.RecordClientReport(new ClientReport(
            DateTimeOffset.UtcNow,
            payload.ClientId ?? "unknown",
            User.Identity?.Name,
            payload.Url ?? string.Empty,
            payload.SignalRState ?? string.Empty,
            payload.ConsecutiveHeartbeatFailures,
            payload.AjaxFailuresCount,
            payload.UnhandledErrorsCount + payload.PromiseRejectionsCount,
            payload.IsOnline,
            payload.RecentErrorLogs?.LastOrDefault()));

        _logger.LogWarning(
            "Browser diagnostics alert received from Client {ClientId} at {Url}. Errors: {Errors}, AJAX Failures: {AjaxFailures}, ConsecutiveHeartbeatFailures: {HeartbeatFailures}, SignalRState: {SignalRState}",
            payload.ClientId, payload.Url, payload.UnhandledErrorsCount, payload.AjaxFailuresCount, payload.ConsecutiveHeartbeatFailures, payload.SignalRState);

        return Ok(new { status = "recorded" });
    }

    private bool DiagnosticsEnabled()
    {
        var options = _options.CurrentValue;
        return options.Enabled && options.EnableDiagnosticsApi;
    }
}
