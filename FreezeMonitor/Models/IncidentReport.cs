namespace SignalRMVC.FreezeMonitor.Models;

public sealed record IncidentReport(
    string IncidentId,
    DateTimeOffset TimestampUtc,
    string Reason,
    string DirectoryPath,
    string MachineName,
    int ProcessId,
    EnvironmentInformation Environment,
    HealthSnapshot Health,
    RequestStatistics Statistics,
    SignalRSnapshot SignalR,
    ThreadPoolSnapshot ThreadPool,
    SqlHealthSnapshot Sql,
    HealthStatusLevel StatusLevel = HealthStatusLevel.FreezeDetected,
    List<string>? CapturedFiles = null)
{
    public IncidentReport(
        DateTimeOffset timestampUtc,
        string reason,
        string directoryPath,
        string machineName,
        int processId,
        HealthSnapshot health,
        RequestStatistics statistics,
        SignalRSnapshot signalR,
        ThreadPoolSnapshot threadPool,
        SqlHealthSnapshot sql)
        : this(
            $"INC-{timestampUtc.UtcDateTime:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}",
            timestampUtc,
            reason,
            directoryPath,
            machineName,
            processId,
            new EnvironmentInformation(
                typeof(IncidentReport).Assembly.GetName().Version?.ToString() ?? "1.0.0",
                machineName,
                System.Environment.OSVersion.ToString(),
                System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                DateTimeOffset.UtcNow,
                TimeSpan.FromSeconds(health.ApplicationUptimeSeconds),
                processId,
                System.Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"),
            health,
            statistics,
            signalR,
            threadPool,
            sql,
            HealthStatusLevel.FreezeDetected,
            new List<string>())
    {
    }
}
