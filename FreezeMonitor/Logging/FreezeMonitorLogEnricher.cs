using Serilog.Core;
using Serilog.Events;

namespace SignalRMVC.FreezeMonitor.Logging;

public sealed class FreezeMonitorLogEnricher : ILogEventEnricher
{
    private static string? _incidentId;
    private static string? _correlationId;

    public static void SetCurrentIncidentContext(string? incidentId, string? correlationId = null)
    {
        _incidentId = incidentId;
        _correlationId = correlationId;
    }

    public static void ClearCurrentIncidentContext()
    {
        _incidentId = null;
        _correlationId = null;
    }

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("MachineName", Environment.MachineName));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("ProcessId", Environment.ProcessId));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("ApplicationVersion", typeof(FreezeMonitorLogEnricher).Assembly.GetName().Version?.ToString() ?? "1.0.0"));
        logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("Environment", Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"));

        if (!string.IsNullOrWhiteSpace(_incidentId))
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("IncidentId", _incidentId));
        }

        if (!string.IsNullOrWhiteSpace(_correlationId))
        {
            logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty("CorrelationId", _correlationId));
        }
    }
}
