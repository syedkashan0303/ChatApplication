namespace SignalRMVC.FreezeMonitor.Configuration;

public sealed class SmtpOptions
{
    public bool Enabled { get; set; } = true;
    public string Host { get; set; } = "server86.hndservers.net";
    public int Port { get; set; } = 587;
    public bool UseSSL { get; set; } = true;
    public string Username { get; set; } = "syed.kashan@aryservices.com.pk";
    public string Password { get; set; } = "QF2((h5Qp3O$";
    public string FromEmail { get; set; } = "syed.kashan@aryservices.com.pk";
    public string FromName { get; set; } = "Freeze Monitor";
    public List<string> ToEmails { get; set; } = new List<string> { "sayedkashan58@gmail.com" };
}
