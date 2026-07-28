namespace SignalRMVC.FreezeMonitor.Models;

public sealed record SignalRSnapshot(
    int CurrentConnections,
    int ConnectedUsers,
    int PeakConnections,
    long ConnectionsCreatedToday,
    long ConnectionsClosedToday,
    double AverageConnectionDurationSeconds);
