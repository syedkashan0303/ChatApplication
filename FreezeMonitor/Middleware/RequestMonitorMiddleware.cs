using System.Diagnostics;
using Microsoft.Extensions.Options;
using SignalRMVC.FreezeMonitor.Configuration;
using SignalRMVC.FreezeMonitor.Services;

namespace SignalRMVC.FreezeMonitor.Middleware;

public sealed class RequestMonitorMiddleware
{
    private readonly RequestDelegate _next;
    private readonly RequestStatisticsService _statisticsService;
    private readonly IOptionsMonitor<FreezeMonitorOptions> _options;
    private readonly ILogger _logger;

    public RequestMonitorMiddleware(
        RequestDelegate next,
        RequestStatisticsService statisticsService,
        IOptionsMonitor<FreezeMonitorOptions> options,
        ILoggerFactory loggerFactory)
    {
        _next = next;
        _statisticsService = statisticsService;
        _options = options;
        _logger = loggerFactory.CreateLogger("FreezeMonitor");
    }

    // WebSockets (HTTP/1.1 upgrade or HTTP/2 CONNECT) and SignalR streaming transports stay open for as long as the
    // user keeps the chat page open. Counting them would show every connected user as a stuck request.
    private static bool IsLongLivedConnection(HttpContext context)
    {
        if (context.WebSockets.IsWebSocketRequest || HttpMethods.IsConnect(context.Request.Method))
        {
            return true;
        }

        var path = context.Request.Path;
        return path.StartsWithSegments("/hubs") && !path.Value!.EndsWith("/negotiate", StringComparison.OrdinalIgnoreCase);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var options = _options.CurrentValue;
        if (!options.Enabled || IsLongLivedConnection(context))
        {
            await _next(context);
            return;
        }

        var requestId = context.TraceIdentifier;
        var requestPath = context.Request.PathBase.Add(context.Request.Path).Value ?? "/";
        _statisticsService.StartRequest(
            requestId,
            context.Request.Method,
            requestPath,
            context.User.Identity?.Name,
            context.Connection.Id);

        try
        {
            await _next(context);
            var elapsedMilliseconds = _statisticsService.CompleteRequest(requestId, context.Response.StatusCode);
            LogCompletion(context, requestId, requestPath, elapsedMilliseconds, options);
        }
        catch (Exception exception)
        {
            var elapsedMilliseconds = _statisticsService.CompleteRequest(
                requestId,
                StatusCodes.Status500InternalServerError,
                exception);
            _logger.LogError(
                exception,
                "Request failed: {Method} {Path} RequestId={RequestId} ConnectionId={ConnectionId} ElapsedMs={ElapsedMilliseconds}",
                context.Request.Method,
                requestPath,
                requestId,
                context.Connection.Id,
                elapsedMilliseconds);
            throw;
        }
    }

    private void LogCompletion(
        HttpContext context,
        string requestId,
        string requestPath,
        double elapsedMilliseconds,
        FreezeMonitorOptions options)
    {
        if (context.Response.StatusCode >= StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(
                "Request completed with HTTP {StatusCode}: {Method} {Path} RequestId={RequestId} ConnectionId={ConnectionId} ElapsedMs={ElapsedMilliseconds}",
                context.Response.StatusCode,
                context.Request.Method,
                requestPath,
                requestId,
                context.Connection.Id,
                elapsedMilliseconds);
        }

        if (elapsedMilliseconds >= TimeSpan.FromSeconds(Math.Max(1, options.LongRequestThresholdSeconds)).TotalMilliseconds)
        {
            _logger.LogWarning(
                "Long-running request: {Method} {Path} StatusCode={StatusCode} User={UserName} RequestId={RequestId} ConnectionId={ConnectionId} ThreadId={ThreadId} ElapsedMs={ElapsedMilliseconds}",
                context.Request.Method,
                requestPath,
                context.Response.StatusCode,
                context.User.Identity?.Name,
                requestId,
                context.Connection.Id,
                Environment.CurrentManagedThreadId,
                elapsedMilliseconds);
        }
    }
}
