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

        // Async so it never blocks a ThreadPool thread. Previously void + sync ADO.NET
        // caused ThreadPool starvation when the SP ran while users were active.
        public async Task RunStoredProcedureAsync(CancellationToken cancellationToken = default)
        {
            var sw = Stopwatch.StartNew();

            string connectionString = _configuration.GetConnectionString("AppDbContextConnection")
                ?? throw new InvalidOperationException("Connection string 'AppDbContextConnection' not found.");

            await using var conn = new SqlConnection(connectionString);
            await using var cmd = new SqlCommand("sp_ArchiveOldChatData", conn)
            {
                CommandType = CommandType.StoredProcedure,
                CommandTimeout = 20  // 5-minute timeout for large batch archiving
            };
            cmd.Parameters.AddWithValue("@DaysToKeep", 2);

            await conn.OpenAsync(cancellationToken);
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            sw.Stop();
            _logger.LogInformation(
                "sp_ArchiveOldChatData completed successfully in {ElapsedMs}ms", sw.ElapsedMilliseconds);
        }
    }
}
