namespace SignalRMVC.CustomClasses
{
    public class ScheduledTaskService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public ScheduledTaskService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
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
