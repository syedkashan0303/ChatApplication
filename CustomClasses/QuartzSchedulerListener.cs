using Quartz;
using Quartz.Listener;

namespace SignalRMVC.CustomClasses
{
    public class QuartzSchedulerListener : SchedulerListenerSupport
    {
        private readonly ILogger<QuartzSchedulerListener> _logger;

        public QuartzSchedulerListener(ILogger<QuartzSchedulerListener> logger)
        {
            _logger = logger;
        }

        public override Task SchedulerStarted(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Scheduler Started");
            return Task.CompletedTask;
        }

        public override Task SchedulerShutdown(CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("Scheduler Shutdown");
            return Task.CompletedTask;
        }

        public override Task SchedulerError(string msg, SchedulerException cause, CancellationToken cancellationToken = default)
        {
            _logger.LogError(cause, "Exception Details: Scheduler error occurred: {Message}", msg);
            return Task.CompletedTask;
        }
    }
}
