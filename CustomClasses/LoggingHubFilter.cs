using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace SignalRMVC.CustomClasses
{
    public class LoggingHubFilter : IHubFilter
    {
        private readonly ILogger<LoggingHubFilter> _logger;

        public LoggingHubFilter(ILogger<LoggingHubFilter> logger)
        {
            _logger = logger;
        }

        public async ValueTask<object?> InvokeMethodAsync(
            HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
        {
            var sw = Stopwatch.StartNew();
            var hubMethodName = invocationContext.HubMethodName;
            var username = invocationContext.Context.User?.Identity?.Name ?? "Anonymous";

            try
            {
                var result = await next(invocationContext);
                sw.Stop();

                _logger.LogInformation("Hub Call: {HubMethod} User: {Username} Time: {ExecutionTime}ms", 
                    hubMethodName, username, sw.ElapsedMilliseconds);

                return result;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex, "Hub Call Error: {HubMethod} User: {Username} Time: {ExecutionTime}ms", 
                    hubMethodName, username, sw.ElapsedMilliseconds);
                throw;
            }
        }
    }
}
