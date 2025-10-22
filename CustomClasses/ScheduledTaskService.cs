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

        /// 
        /// 
        /// this code for reStart the application
        /// 
        /// 


        //protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        //{
        //    _logger.LogInformation("ScheduledTaskService started.");

        //    while (!stoppingToken.IsCancellationRequested)
        //    {
        //        var now = DateTime.Now;
        //        var nextRunTime = new DateTime(now.Year, now.Month, now.Day, 22, 0, 0); // Today at 10 PM
        //        if (now > nextRunTime)
        //        {
        //            nextRunTime = nextRunTime.AddDays(1); // Tomorrow 10 PM
        //        }

        //        var delay = nextRunTime - now;
        //        _logger.LogInformation($"ScheduledTaskService delaying {delay} until next run time at {nextRunTime}.");

        //        try
        //        {
        //            //await Task.Delay(delay, stoppingToken);
        //            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        //        }
        //        catch (TaskCanceledException)
        //        {
        //            // Service is stopping
        //            break;
        //        }

        //        // Run your scheduled job here
        //        try
        //        {
        //            using (var scope = _scopeFactory.CreateScope())
        //            {
        //                var jobService = scope.ServiceProvider.GetRequiredService<DatabaseJobService>();
        //                jobService.RunStoredProcedure();  // your custom logic before shutdown
        //            }

        //            _logger.LogInformation("ScheduledTaskService running shutdown sequence at 10 PM.");

        //            // Trigger graceful app shutdown to restart SignalR hub
        //            _appLifetime.StopApplication();
        //        }
        //        catch (Exception ex)
        //        {
        //            _logger.LogError(ex, "Error running scheduled task.");
        //        }
        //    }

        //    _logger.LogInformation("ScheduledTaskService stopping.");
        //}

    }

}
