using System.Net;
using System.Net.Mail;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SignalRMVC.FreezeMonitor.Configuration;
using SignalRMVC.FreezeMonitor.Models;

namespace SignalRMVC.FreezeMonitor.Services;

public sealed class EmailNotificationService
{
    private readonly IOptionsMonitor<FreezeMonitorOptions> _options;
    private readonly ILogger _logger;

    public EmailNotificationService(
        IOptionsMonitor<FreezeMonitorOptions> options,
        ILoggerFactory loggerFactory)
    {
        _options = options;
        _logger = loggerFactory.CreateLogger("FreezeMonitor");
    }

    public async Task SendIncidentEmailAsync(IncidentReport incidentReport, CancellationToken cancellationToken = default)
    {
        var smtpOptions = _options.CurrentValue.SMTP;
        if (!smtpOptions.Enabled)
        {
            _logger.LogInformation("SMTP email notifications are disabled.");
            return;
        }

        if (string.IsNullOrWhiteSpace(smtpOptions.Host) || smtpOptions.ToEmails == null || !smtpOptions.ToEmails.Any(e => !string.IsNullOrWhiteSpace(e)))
        {
            _logger.LogWarning("SMTP Host or ToEmails recipient list is not properly configured. Email skipped.");
            return;
        }

        try
        {
            using var mailMessage = new MailMessage();
            mailMessage.From = new MailAddress(
                string.IsNullOrWhiteSpace(smtpOptions.FromEmail) ? "freezemonitor@local" : smtpOptions.FromEmail,
                string.IsNullOrWhiteSpace(smtpOptions.FromName) ? "Freeze Monitor" : smtpOptions.FromName);

            foreach (var recipient in smtpOptions.ToEmails.Where(e => !string.IsNullOrWhiteSpace(e)))
            {
                mailMessage.To.Add(recipient.Trim());
            }

            mailMessage.Subject = $"[FREEZE MONITOR ALERT] Incident {incidentReport.IncidentId} - {incidentReport.MachineName} ({incidentReport.StatusLevel})";
            mailMessage.IsBodyHtml = true;
            mailMessage.Body = BuildHtmlBody(incidentReport);

            // Attach Summary.json if summary file exists in incident directory
            var summaryPath = Path.Combine(incidentReport.DirectoryPath, "Summary.json");
            Attachment? attachment = null;
            if (File.Exists(summaryPath))
            {
                attachment = new Attachment(summaryPath, "application/json");
                mailMessage.Attachments.Add(attachment);
            }

            using var client = new SmtpClient(smtpOptions.Host, smtpOptions.Port > 0 ? smtpOptions.Port : 587)
            {
                EnableSsl = smtpOptions.UseSSL,
                DeliveryMethod = SmtpDeliveryMethod.Network,
                UseDefaultCredentials = false
            };

            if (!string.IsNullOrWhiteSpace(smtpOptions.Username))
            {
                client.Credentials = new NetworkCredential(smtpOptions.Username, smtpOptions.Password ?? string.Empty);
            }

            await client.SendMailAsync(mailMessage, cancellationToken);
            _logger.LogInformation("Incident notification email successfully sent for Incident ID {IncidentId}", incidentReport.IncidentId);

            attachment?.Dispose();
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to send incident email notification for Incident ID {IncidentId}", incidentReport.IncidentId);
        }
    }

    private static string BuildHtmlBody(IncidentReport incident)
    {
        var h = incident.Health;
        var env = incident.Environment;
        var sql = incident.Sql;
        var tp = incident.ThreadPool;
        var sig = incident.SignalR;

        var appVersion = env?.ApplicationVersion ?? "1.0.0";
        var envName = env?.EnvironmentName ?? "Production";
        var osVersion = env?.OsVersion ?? "--";
        var runtimeVersion = env?.DotNetRuntimeVersion ?? "--";
        var uptimeHours = env != null ? env.ApplicationUptime.TotalHours.ToString("F2") : (h != null ? TimeSpan.FromSeconds(h.ApplicationUptimeSeconds).TotalHours.ToString("F2") : "0");

        var minWorker = tp != null ? tp.MinimumWorkerThreads : 0;
        var maxWorker = tp != null ? tp.MaximumWorkerThreads : 0;
        var availWorker = tp != null ? tp.AvailableWorkerThreads : 0;
        var availIo = tp != null ? tp.AvailableIoThreads : 0;

        var sqlStatus = sql != null && sql.IsHealthy ? "HEALTHY" : "FAILED";
        var sqlTime = sql != null ? sql.TotalTimeMilliseconds.ToString("F1") : "0";

        var sigConn = sig != null ? sig.CurrentConnections : (h != null ? h.ActiveSignalRConnections : 0);
        var sigUsers = sig != null ? sig.ConnectedUsers : (h != null ? h.ConnectedUsers : 0);

        var cpu = h != null ? h.CpuUsagePercent.ToString("F1") : "0";
        var workingSetMb = h != null ? (h.WorkingSetBytes / (1024 * 1024)).ToString("N0") : "0";
        var pendingReq = h != null ? h.PendingRequests : 0;

        return $@"
        <!DOCTYPE html>
        <html>
        <head>
            <style>
                body {{ font-family: Arial, sans-serif; background-color: #1a1d21; color: #e1e1e1; padding: 20px; }}
                .card {{ background-color: #24282e; border-radius: 8px; padding: 20px; margin-bottom: 15px; border: 1px solid #363c44; }}
                .badge-alert {{ background-color: #dc3545; color: white; padding: 4px 10px; border-radius: 4px; font-weight: bold; }}
                table {{ width: 100%; border-collapse: collapse; margin-top: 10px; }}
                th, td {{ padding: 8px 12px; border-bottom: 1px solid #363c44; text-align: left; }}
                th {{ background-color: #2c323b; color: #a0a0a0; }}
            </style>
        </head>
        <body>
            <div class=""card"">
                <h2>🚨 Freeze Monitor Alert - <span class=""badge-alert"">{incident.StatusLevel}</span></h2>
                <p><strong>Incident ID:</strong> {incident.IncidentId}</p>
                <p><strong>Reason:</strong> {incident.Reason}</p>
                <p><strong>Timestamp:</strong> {incident.TimestampUtc:yyyy-MM-dd HH:mm:ss} UTC</p>
                <p><strong>Incident Directory:</strong> <code>{incident.DirectoryPath}</code></p>
            </div>

            <div class=""card"">
                <h3>Environment & System Metrics</h3>
                <table>
                    <tr><th>Machine Name</th><td>{incident.MachineName}</td></tr>
                    <tr><th>Application Version</th><td>{appVersion}</td></tr>
                    <tr><th>Environment</th><td>{envName}</td></tr>
                    <tr><th>OS Version</th><td>{osVersion}</td></tr>
                    <tr><th>.NET Runtime</th><td>{runtimeVersion}</td></tr>
                    <tr><th>Application Uptime</th><td>{uptimeHours} hours</td></tr>
                    <tr><th>CPU Usage</th><td>{cpu}%</td></tr>
                    <tr><th>Memory (Working Set)</th><td>{workingSetMb} MB</td></tr>
                    <tr><th>ThreadPool Worker Threads</th><td>{availWorker} available / {minWorker} min / {maxWorker} max</td></tr>
                    <tr><th>ThreadPool Completion Port</th><td>{availIo} available</td></tr>
                    <tr><th>SQL Status</th><td>{sqlStatus} ({sqlTime} ms)</td></tr>
                    <tr><th>SignalR Connections</th><td>{sigConn} active connections ({sigUsers} users)</td></tr>
                    <tr><th>Pending Requests</th><td>{pendingReq}</td></tr>
                </table>
            </div>

            <p style=""font-size: 12px; color: #888;"">This is an automated operational alert generated by Production Freeze Monitor.</p>
        </body>
        </html>
        ";
    }
}
