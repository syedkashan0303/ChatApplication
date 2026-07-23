using System.Globalization;

namespace SignalRMVC.CustomClasses
{
    public static class QuartzCronHelper
    {
        public static (int Hour, int Minute, string CronExpression) ValidateAndBuildCron(string? executionTime)
        {
            if (string.IsNullOrWhiteSpace(executionTime))
            {
                throw new InvalidOperationException(
                    "Invalid QuartzSettings configuration: 'DailyExecutionTime' cannot be null or empty. Expected 24-hour HH:mm format (e.g. '03:00').");
            }

            if (!TimeOnly.TryParseExact(executionTime.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
            {
                throw new InvalidOperationException(
                    $"Invalid QuartzSettings configuration: 'DailyExecutionTime' value '{executionTime}' is not in valid 24-hour HH:mm format. Must be between 00:00 and 23:59.");
            }

            string cronExpression = $"0 {time.Minute} {time.Hour} * * ?";
            return (time.Hour, time.Minute, cronExpression);
        }
    }
}
