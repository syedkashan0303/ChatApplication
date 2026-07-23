using Quartz;

namespace SignalRMVC.CustomClasses
{
    [DisallowConcurrentExecution]
    public class DailyDatabaseJob : IJob
    {
        private readonly DatabaseJobService _databaseJobService;
        private readonly ILogger<DailyDatabaseJob> _logger;

        public DailyDatabaseJob(DatabaseJobService databaseJobService, ILogger<DailyDatabaseJob> logger)
        {
            _databaseJobService = databaseJobService;
            _logger = logger;
        }

        public async Task Execute(IJobExecutionContext context)
        {
            _logger.LogInformation("Job Started");
            _logger.LogInformation("Job Triggered");

            // Level 2 Timeout Protection: Linked CancellationTokenSource with 5-minute maximum execution limit
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
            linkedCts.CancelAfter(TimeSpan.FromMinutes(5));

            try
            {
                await _databaseJobService.RunStoredProcedureAsync(linkedCts.Token);
            }
            catch (Exception ex)
            {
                // Guarantee scheduler survival without immediate retry
                _logger.LogError(ex, "Exception Details:\n{Exception}", ex.ToString());
            }
            finally
            {
                var nextRun = context.NextFireTimeUtc?.ToLocalTime();
                if (nextRun.HasValue)
                {
                    _logger.LogInformation("Next Scheduled Run Time: {NextRun:yyyy-MM-dd HH:mm:ss zzz}", nextRun.Value);
                }
                else
                {
                    _logger.LogInformation("Next Scheduled Run Time: None");
                }
            }
        }
    }
}
