namespace SignalRMVC.FreezeMonitor.Models;

public sealed record ThreadPoolSnapshot(
    int AvailableWorkerThreads,
    int AvailableIoThreads,
    int MaximumWorkerThreads,
    int MaximumIoThreads,
    int MinimumWorkerThreads,
    int MinimumIoThreads,
    int WorkerThreadsInUse,
    int IoThreadsInUse,
    int ThreadCount);
