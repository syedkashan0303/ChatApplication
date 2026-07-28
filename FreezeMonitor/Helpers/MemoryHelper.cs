namespace SignalRMVC.FreezeMonitor.Helpers;

public static class MemoryHelper
{
    public static long GetManagedMemoryBytes() => GC.GetTotalMemory(forceFullCollection: false);
}
