using System.Diagnostics;

using Microsoft.Extensions.Options;
using SignalRMVC.FreezeMonitor.Configuration;

namespace SignalRMVC.FreezeMonitor.Services;

public sealed class ProcDumpService
{
    private readonly IOptionsMonitor<FreezeMonitorOptions> _options;
    private readonly ILogger _logger;

    public ProcDumpService(
        IOptionsMonitor<FreezeMonitorOptions> options,
        ILoggerFactory loggerFactory)
    {
        _options = options;
        _logger = loggerFactory.CreateLogger("FreezeMonitor");
    }

    public async Task<bool> ExecuteProcDumpAsync(string outputDirectory, CancellationToken cancellationToken = default)
    {
        var procDumpOptions = _options.CurrentValue.ProcDump;
        if (!procDumpOptions.Enabled)
        {
            _logger.LogInformation("ProcDump integration is disabled in configuration.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(procDumpOptions.ExecutablePath) || !File.Exists(procDumpOptions.ExecutablePath))
        {
            _logger.LogWarning("ProcDump executable not found at specified path: {ExecutablePath}", procDumpOptions.ExecutablePath);
            return false;
        }

        var processId = Environment.ProcessId;
        var dumpPath = Path.Combine(outputDirectory, "dump.dmp");
        var arguments = procDumpOptions.Arguments
            .Replace("{PID}", processId.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{OUTPUT}", $"\"{dumpPath}\"", StringComparison.OrdinalIgnoreCase);

        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("Starting ProcDump execution for Process ID {PID}. Arguments: {Arguments}", processId, arguments);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = procDumpOptions.ExecutablePath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                _logger.LogError("Failed to start ProcDump process.");
                return false;
            }

            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(Math.Max(5, procDumpOptions.TimeoutSeconds)), cancellationToken);
            var processExitTask = process.WaitForExitAsync(cancellationToken);

            var completedTask = await Task.WhenAny(processExitTask, timeoutTask);
            stopwatch.Stop();

            if (completedTask == timeoutTask)
            {
                _logger.LogError("ProcDump execution timed out after {ElapsedSeconds}s. Attempting to terminate ProcDump process.", stopwatch.Elapsed.TotalSeconds);
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception killEx)
                {
                    _logger.LogWarning(killEx, "Error killing timed-out ProcDump process.");
                }

                return false;
            }

            var stdOut = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stdErr = await process.StandardError.ReadToEndAsync(cancellationToken);
            var exitCode = process.ExitCode;

            if (exitCode == 0)
            {
                _logger.LogCritical(
                    "ProcDump successfully created process dump at {DumpPath} in {ElapsedSeconds:F2}s. ExitCode: 0",
                    dumpPath, stopwatch.Elapsed.TotalSeconds);
                return true;
            }

            _logger.LogError(
                "ProcDump finished with non-zero ExitCode {ExitCode} in {ElapsedSeconds:F2}s. StdOut: {StdOut}, StdErr: {StdErr}",
                exitCode, stopwatch.Elapsed.TotalSeconds, stdOut, stdErr);
            return false;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            _logger.LogError(exception, "ProcDump execution failed unexpectedly after {ElapsedSeconds:F2}s", stopwatch.Elapsed.TotalSeconds);
            return false;
        }
    }
}
