using System.Diagnostics;

namespace SignalRMVC.FreezeMonitor.Helpers;

public static class ProcessHelper
{
    public static TimeSpan GetUptime(Process process, DateTimeOffset nowUtc)
    {
        try
        {
            return nowUtc - process.StartTime.ToUniversalTime();
        }
        catch (InvalidOperationException)
        {
            return TimeSpan.Zero;
        }
    }
}
