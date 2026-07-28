using System.Diagnostics;

namespace SignalRMVC.FreezeMonitor.Services;

public sealed class CpuUsageService
{
    private readonly object _sync = new();
    private TimeSpan? _previousProcessorTime;
    private DateTimeOffset? _previousSampleUtc;

    public double GetUsagePercent(Process process, DateTimeOffset nowUtc)
    {
        lock (_sync)
        {
            var processorTime = process.TotalProcessorTime;
            if (_previousProcessorTime is null || _previousSampleUtc is null)
            {
                _previousProcessorTime = processorTime;
                _previousSampleUtc = nowUtc;
                return 0;
            }

            var elapsed = nowUtc - _previousSampleUtc.Value;
            var processorElapsed = processorTime - _previousProcessorTime.Value;
            _previousProcessorTime = processorTime;
            _previousSampleUtc = nowUtc;

            if (elapsed <= TimeSpan.Zero)
            {
                return 0;
            }

            var usage = processorElapsed.TotalMilliseconds /
                        (elapsed.TotalMilliseconds * Environment.ProcessorCount) * 100;
            return Math.Round(Math.Max(0, usage), 2);
        }
    }
}
