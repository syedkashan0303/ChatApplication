using Microsoft.AspNetCore.SignalR;
using SignalRMVC.FreezeMonitor.Services;
using System.Diagnostics;

namespace SignalRMVC.CustomClasses
{
    public class LoggingHubFilter : IHubFilter
    {
        private const long SlowMethodThresholdMs = 2_000;

        private readonly ILogger<LoggingHubFilter> _logger;
        private readonly SignalRHealthService _signalRHealthService;
        private readonly HubDiagnosticsService _hubDiagnostics;

        public LoggingHubFilter(ILogger<LoggingHubFilter> logger, SignalRHealthService signalRHealthService, HubDiagnosticsService hubDiagnostics)
        {
            _logger = logger;
            _signalRHealthService = signalRHealthService;
            _hubDiagnostics = hubDiagnostics;
        }

        public async ValueTask<object?> InvokeMethodAsync(
            HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
        {
            var sw = Stopwatch.StartNew();
            var method = invocationContext.HubMethodName;
            var user = invocationContext.Context.User?.Identity?.Name ?? "Anonymous";
            var connId = invocationContext.Context.ConnectionId;
            _signalRHealthService.RecordActivity(connId);

            try
            {
                var result = await next(invocationContext);
                sw.Stop();
                _hubDiagnostics.RecordInvocation(method, user, sw.Elapsed.TotalMilliseconds, null);

                if (sw.ElapsedMilliseconds > SlowMethodThresholdMs)
                    _logger.LogWarning(
                        "SLOW Hub Method | {HubMethod} | User: {Username} | Conn: {ConnId} | {ElapsedMs}ms",
                        method, user, connId, sw.ElapsedMilliseconds);
                else
                    _logger.LogDebug(
                        "Hub Method | {HubMethod} | User: {Username} | {ElapsedMs}ms",
                        method, user, sw.ElapsedMilliseconds);

                return result;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _hubDiagnostics.RecordInvocation(method, user, sw.Elapsed.TotalMilliseconds, ex);
                _logger.LogError(ex,
                    "Hub Method Error | {HubMethod} | User: {Username} | Conn: {ConnId} | {ElapsedMs}ms",
                    method, user, connId, sw.ElapsedMilliseconds);
                throw;
            }
        }
    }
}
