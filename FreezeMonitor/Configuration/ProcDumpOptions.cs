namespace SignalRMVC.FreezeMonitor.Configuration;

public sealed class ProcDumpOptions
{
    public bool Enabled { get; set; } = false;
    public string ExecutablePath { get; set; } = @"C:\Tools\ProcDump\procdump.exe";
    public string Arguments { get; set; } = "-ma {PID} {OUTPUT}";
    public int TimeoutSeconds { get; set; } = 120;
}
