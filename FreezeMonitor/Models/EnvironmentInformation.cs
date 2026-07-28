namespace SignalRMVC.FreezeMonitor.Models;

public sealed record EnvironmentInformation(
    string ApplicationVersion,
    string MachineName,
    string OsVersion,
    string DotNetRuntimeVersion,
    DateTimeOffset ServerTimeUtc,
    TimeSpan ApplicationUptime,
    int ProcessId,
    string EnvironmentName
);
