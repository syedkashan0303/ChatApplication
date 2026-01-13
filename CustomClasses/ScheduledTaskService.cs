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
            while (!stoppingToken.IsCancellationRequested)
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var jobService = scope.ServiceProvider.GetRequiredService<DatabaseJobService>();
                    jobService.RunStoredProcedure();
                }

                // Wait 15 hours
                await Task.Delay(TimeSpan.FromHours(2), stoppingToken);
                //await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }

}
