using Microsoft.Data.SqlClient;
using System.Data;
using System.Diagnostics;

namespace SignalRMVC.CustomClasses
{
    public class DatabaseJobService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<DatabaseJobService> _logger;

        public DatabaseJobService(IConfiguration configuration, ILogger<DatabaseJobService> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public async Task RunStoredProcedureAsync(CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();

            _logger.LogInformation("Stored Procedure Started");

            string connectionString = _configuration.GetConnectionString("AppDbContextConnection")
                ?? throw new InvalidOperationException("Connection string 'AppDbContextConnection' not found.");

            try
            {
                await using var conn = new SqlConnection(connectionString);
                await using var cmd = new SqlCommand("sp_ArchiveOldChatData", conn)
                {
                    CommandType = CommandType.StoredProcedure,
                    CommandTimeout = 300 // Level 1 Timeout Protection: 300 seconds (5 minutes)
                };
                cmd.Parameters.AddWithValue("@DaysToKeep", 2);

                await conn.OpenAsync(cancellationToken);
                await cmd.ExecuteNonQueryAsync(cancellationToken);

                sw.Stop();

                _logger.LogInformation("Stored Procedure Finished");
                _logger.LogInformation("Execution Time:\n{Duration}", FormatDuration(sw.Elapsed));
                _logger.LogInformation("Job Completed Successfully");
            }
            catch (SqlException ex) when (IsTimeoutException(ex))
            {
                sw.Stop();
                _logger.LogWarning("Timeout Occurred");
                _logger.LogWarning("Execution Time:\n{Duration}", FormatDuration(sw.Elapsed));
                _logger.LogWarning("Job Timed Out");
                _logger.LogError(ex, "Exception Details:\n{Exception}", ex.ToString());
            }
            catch (OperationCanceledException ex)
            {
                sw.Stop();
                _logger.LogWarning("Timeout Occurred");
                _logger.LogWarning("Execution Time:\n{Duration}", FormatDuration(sw.Elapsed));
                _logger.LogWarning("Job Timed Out");
                _logger.LogError(ex, "Exception Details:\n{Exception}", ex.ToString());
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError("Execution Time:\n{Duration}", FormatDuration(sw.Elapsed));
                _logger.LogError(ex, "Exception Details:\n{Exception}", ex.ToString());
            }
        }

        private static bool IsTimeoutException(SqlException ex)
        {
            return ex.Number == -2 || ex.Message.Contains("Timeout", StringComparison.OrdinalIgnoreCase);
        }

        private static string FormatDuration(TimeSpan timeSpan)
        {
            if (timeSpan.TotalMinutes >= 1)
            {
                int minutes = (int)Math.Round(timeSpan.TotalMinutes);
                return $"{minutes} minute{(minutes == 1 ? "" : "s")}";
            }

            int seconds = (int)Math.Round(timeSpan.TotalSeconds);
            return $"{seconds} second{(seconds == 1 ? "" : "s")}";
        }
    }
}
