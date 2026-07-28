using System.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using SignalRMVC.FreezeMonitor.Configuration;
using SignalRMVC.FreezeMonitor.Models;

namespace SignalRMVC.FreezeMonitor.Services;

public sealed class SqlHealthChecker
{
    private readonly string _connectionString;
    private readonly IOptionsMonitor<FreezeMonitorOptions> _options;
    private readonly ILogger _logger;
    private SqlHealthSnapshot? _latestSnapshot;

    public SqlHealthChecker(
        IConfiguration configuration,
        IOptionsMonitor<FreezeMonitorOptions> options,
        ILoggerFactory loggerFactory)
    {
        _connectionString = configuration.GetConnectionString("AppDbContextConnection")
            ?? throw new InvalidOperationException("Connection string 'AppDbContextConnection' not found.");
        _options = options;
        _logger = loggerFactory.CreateLogger("FreezeMonitor");
    }

    public SqlHealthSnapshot? GetLatestSnapshot() => Volatile.Read(ref _latestSnapshot);

    public async Task<SqlHealthSnapshot> CheckAsync(CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var startedAtUtc = DateTimeOffset.UtcNow;
        var totalTimer = Stopwatch.StartNew();
        var connectionTimer = Stopwatch.StartNew();

        try
        {
            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, options.SqlHealthTimeoutSeconds)));

            await using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(timeoutSource.Token);
            connectionTimer.Stop();

            var executionTimer = Stopwatch.StartNew();
            await using var command = new SqlCommand("SELECT 1", connection)
            {
                CommandTimeout = Math.Max(1, options.SqlHealthTimeoutSeconds)
            };
            await command.ExecuteScalarAsync(timeoutSource.Token);
            executionTimer.Stop();
            totalTimer.Stop();

            var healthy = new SqlHealthSnapshot(
                startedAtUtc,
                true,
                connectionTimer.Elapsed.TotalMilliseconds,
                executionTimer.Elapsed.TotalMilliseconds,
                totalTimer.Elapsed.TotalMilliseconds,
                false,
                null);
            Volatile.Write(ref _latestSnapshot, healthy);
            return healthy;
        }
        catch (Exception exception)
        {
            totalTimer.Stop();
            if (connectionTimer.IsRunning)
            {
                connectionTimer.Stop();
            }

            var timedOut = exception is OperationCanceledException ||
                           exception is SqlException { Number: -2 };
            var failed = new SqlHealthSnapshot(
                startedAtUtc,
                false,
                connectionTimer.Elapsed.TotalMilliseconds,
                0,
                totalTimer.Elapsed.TotalMilliseconds,
                timedOut,
                exception.Message);
            Volatile.Write(ref _latestSnapshot, failed);
            _logger.LogError(exception, "SQL health check failed after {ElapsedMilliseconds}ms", totalTimer.Elapsed.TotalMilliseconds);
            return failed;
        }
    }
}
