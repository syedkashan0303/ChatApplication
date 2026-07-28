using System.Diagnostics;
using SignalRMVC.FreezeMonitor.Configuration;
using SignalRMVC.FreezeMonitor.Helpers;
using SignalRMVC.FreezeMonitor.Models;

namespace SignalRMVC.FreezeMonitor.Services;

public sealed class SystemHealthCollector
{
    private readonly CpuUsageService _cpuUsageService;
    private readonly SqlHealthChecker _sqlHealthChecker;
    private readonly SignalRHealthService _signalRHealthService;
    private readonly RequestStatisticsService _requestStatisticsService;
    private readonly IHostEnvironment _hostEnvironment;
    private readonly IConfiguration _configuration;

    public SystemHealthCollector(
        CpuUsageService cpuUsageService,
        SqlHealthChecker sqlHealthChecker,
        SignalRHealthService signalRHealthService,
        RequestStatisticsService requestStatisticsService,
        IHostEnvironment hostEnvironment,
        IConfiguration configuration)
    {
        _cpuUsageService = cpuUsageService;
        _sqlHealthChecker = sqlHealthChecker;
        _signalRHealthService = signalRHealthService;
        _requestStatisticsService = requestStatisticsService;
        _hostEnvironment = hostEnvironment;
        _configuration = configuration;
    }

    public Task<HealthSnapshot> CollectAsync(FreezeMonitorOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var nowUtc = DateTimeOffset.UtcNow;
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        ThreadPool.GetAvailableThreads(out var availableWorker, out var availableIo);
        ThreadPool.GetMaxThreads(out var maximumWorker, out var maximumIo);
        ThreadPool.GetMinThreads(out var minimumWorker, out var minimumIo);

        var threadPool = new ThreadPoolSnapshot(
            availableWorker,
            availableIo,
            maximumWorker,
            maximumIo,
            minimumWorker,
            minimumIo,
            maximumWorker - availableWorker,
            maximumIo - availableIo,
            process.Threads.Count);
        var signalR = _signalRHealthService.GetSnapshot();
        var requests = _requestStatisticsService.GetStatistics(
            TimeSpan.FromSeconds(Math.Max(1, options.LongRequestThresholdSeconds)),
            options.MaxLongRequests);
        var sql = _sqlHealthChecker.GetLatestSnapshot() ?? new SqlHealthSnapshot(
            nowUtc,
            false,
            0,
            0,
            0,
            false,
            "No SQL health sample has completed.");
        var lastError = requests.LastError ?? (!sql.IsHealthy ? sql.ErrorMessage : null);

        var snapshot = new HealthSnapshot(
            nowUtc,
            Environment.MachineName,
            nowUtc.ToLocalTime(),
            process.Id,
            ProcessHelper.GetUptime(process, nowUtc).TotalSeconds,
            _cpuUsageService.GetUsagePercent(process, nowUtc),
            process.WorkingSet64,
            process.PrivateMemorySize64,
            MemoryHelper.GetManagedMemoryBytes(),
            process.Threads.Count,
            process.HandleCount,
            signalR.CurrentConnections,
            signalR.ConnectedUsers,
            sql,
            threadPool,
            requests.PendingRequests,
            requests.LongRunningRequests.Count,
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2),
            lastError);

        return Task.FromResult(snapshot);
    }

    public object GetApplicationInformation()
    {
        using var process = Process.GetCurrentProcess();
        var nowUtc = DateTimeOffset.UtcNow;

        return new
        {
            machineName = Environment.MachineName,
            environment = _hostEnvironment.EnvironmentName,
            applicationName = _hostEnvironment.ApplicationName,
            contentRoot = _hostEnvironment.ContentRootPath,
            processId = process.Id,
            processStartTimeUtc = process.StartTime.ToUniversalTime(),
            uptimeSeconds = ProcessHelper.GetUptime(process, nowUtc).TotalSeconds,
            assemblyVersion = typeof(SystemHealthCollector).Assembly.GetName().Version?.ToString(),
            configuredConnection = _configuration.GetConnectionString("AppDbContextConnection") is not null
        };
    }

    public EnvironmentInformation GetEnvironmentInformation()
    {
        using var process = Process.GetCurrentProcess();
        var nowUtc = DateTimeOffset.UtcNow;
        return new EnvironmentInformation(
            typeof(SystemHealthCollector).Assembly.GetName().Version?.ToString() ?? "1.0.0",
            Environment.MachineName,
            Environment.OSVersion.ToString(),
            System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            nowUtc,
            ProcessHelper.GetUptime(process, nowUtc),
            process.Id,
            _hostEnvironment.EnvironmentName);
    }
}
