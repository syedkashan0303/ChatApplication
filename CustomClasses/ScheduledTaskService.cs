namespace SignalRMVC.CustomClasses
{
    public class ScheduledTaskService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IHostApplicationLifetime _appLifetime;
        private readonly ILogger<ScheduledTaskService> _logger;

        public ScheduledTaskService(
            IServiceScopeFactory scopeFactory,
            IHostApplicationLifetime appLifetime,
            ILogger<ScheduledTaskService> logger)
        {
            _scopeFactory = scopeFactory;
            _appLifetime = appLifetime;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ScheduledTaskService started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Starting scheduled cleanup job.");

                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var jobService = scope.ServiceProvider.GetRequiredService<DatabaseJobService>();
                    await jobService.RunStoredProcedureAsync(stoppingToken);
                    _logger.LogInformation("Scheduled cleanup job finished successfully.");
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // App is shutting down — expected, exit cleanly
                    break;
                }
                catch (Exception ex)
                {
                    // Log and continue — previously this exception escaped the while loop
                    // and permanently killed the background service until app restart.
                    _logger.LogError(ex,
                        "Scheduled cleanup job failed. Will retry after 2 hours.");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromHours(2), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            _logger.LogInformation("ScheduledTaskService stopped.");
        }
    }
}
