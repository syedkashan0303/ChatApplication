using Microsoft.Extensions.Options;
using SignalRMVC.FreezeMonitor.Middleware;
using SignalRMVC.FreezeMonitor.Services;

namespace SignalRMVC.FreezeMonitor.Configuration;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddFreezeMonitor(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<FreezeMonitorOptions>()
            .Bind(configuration.GetSection(FreezeMonitorOptions.SectionName))
            .Validate(options => options.SamplingIntervalSeconds >= 1, "SamplingIntervalSeconds must be at least 1.")
            .Validate(options => options.SqlHealthIntervalSeconds >= 1, "SqlHealthIntervalSeconds must be at least 1.")
            .Validate(options => options.SqlHealthTimeoutSeconds >= 1, "SqlHealthTimeoutSeconds must be at least 1.")
            .Validate(options => options.RingBufferSize is >= 1 and <= 10_000, "RingBufferSize must be between 1 and 10000.")
            .Validate(options => options.LongRequestThresholdSeconds >= 1, "LongRequestThresholdSeconds must be at least 1.")
            .Validate(options => options.MaxLongRequests is >= 1 and <= 1_000, "MaxLongRequests must be between 1 and 1000.")
            .Validate(options => !string.IsNullOrWhiteSpace(options.IncidentFolder), "IncidentFolder is required.")
            .ValidateOnStart();

        services.AddSingleton<CpuUsageService>();
        services.AddSingleton<SignalRHealthService>();
        services.AddSingleton<RequestStatisticsService>();
        services.AddSingleton<SqlHealthChecker>();
        services.AddSingleton<SystemHealthCollector>();
        services.AddSingleton<ProcDumpService>();
        services.AddSingleton<EmailNotificationService>();
        services.AddSingleton<FreezeDetectionService>();
        services.AddSingleton<IncidentWriterService>();
        services.AddSingleton<RingBufferService>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<FreezeMonitorOptions>>().Value;
            return new RingBufferService(options.RingBufferSize);
        });

        var initialOptions = configuration.GetSection(FreezeMonitorOptions.SectionName).Get<FreezeMonitorOptions>()
            ?? new FreezeMonitorOptions();
        if (initialOptions.Enabled)
        {
            services.AddHostedService<FreezeMonitorService>();
        }

        return services;
    }

    public static IApplicationBuilder UseFreezeMonitorRequestMonitoring(this IApplicationBuilder application)
    {
        return application.UseMiddleware<RequestMonitorMiddleware>();
    }
}
