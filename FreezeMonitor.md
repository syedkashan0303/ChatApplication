# Production Freeze Monitor

## Purpose

The Freeze Monitor is an isolated, in-process diagnostics, detection, and incident response module for ASP.NET Core applications. It records lightweight health samples, monitors request and SignalR telemetry, detects freeze conditions, captures process dumps, sends alert notifications, tracks browser-side telemetry, and presents a real-time SRE diagnostics dashboard.

It operates with zero impact on chat messaging logic, authentication, authorization, or database schemas.

---

## Phase 3 Architecture

```text
HTTP requests -> RequestMonitorMiddleware -> RequestStatisticsService
                                              |
SignalR connect/activity/disconnect -> SignalRHealthService
                                              |
FreezeMonitorService (PeriodicTimer) -> SystemHealthCollector -> RingBufferService
           |                                  |
           v                                  v
  FreezeDetectionService (Evaluates Rules) -> Status (Healthy/Warning/Critical/FreezeDetected)
           |
           +-(FreezeDetected Event)-> IncidentWriterService
                                            |
                                            +-> JSON Snapshot Files (Health, SQL, ThreadPool, etc.)
                                            +-> ProcDumpService (process memory dump execution)
                                            +-> EmailNotificationService (HTML alert + Summary.json)

Browser (`freeze-monitor.js`) -> Heartbeat GET /diagnostics/heartbeat
                            -> Failure Telemetry POST /diagnostics/browser

Admin Dashboard (`/Admin/Diagnostics`) -> Real-time status UI (5s refresh)
```

---

## Key Features

### 1. Automatic Freeze Detection (`FreezeDetectionService`)
Continuously evaluates multi-condition health rules on each sampling cycle:
- Pending HTTP requests exceeding configured threshold (`PendingRequestThreshold`).
- Consecutive SQL probe failures (`SqlFailureThreshold`).
- Critically low ThreadPool worker threads (`ThreadPoolWorkerThreshold`).
- Consecutive browser/endpoint heartbeat failures (`HeartbeatFailureThreshold`).
- Request stalls (no completed requests for `NoCompletedRequestSeconds` while requests are pending).
- SignalR connection/activity drop while HTTP requests continue.

When freeze conditions are met:
- Calculates `HealthStatusLevel.FreezeDetected`.
- Raises `FreezeDetected` event.
- Invokes `IncidentWriterService` automatically.
- **Does NOT automatically restart the application.**

### 2. Automatic Incident Capture (`IncidentWriterService`)
Generates a unique `IncidentId` (`INC-yyyyMMdd-HHmmss-XXXXXX`) and writes structured diagnostic files inside `Incidents/<Timestamp>`:
- `Health.json`
- `Statistics.json`
- `SignalR.json`
- `ThreadPool.json`
- `SQL.json`
- `Application.json`
- `Environment.json`
- `Summary.json`

### 3. ProcDump Integration (`ProcDumpService`)
- Asynchronously executes `procdump.exe` when configured (`"ProcDump": { "Enabled": true }`).
- Replaces `{PID}` and `{OUTPUT}` placeholder parameters.
- Enforces execution timeout (`TimeoutSeconds`).
- Completely isolated try-catch block: failure or timeout logs error without throwing or blocking web requests.

### 4. SMTP Email Notifications (`EmailNotificationService`)
- Sends HTML email notification upon incident creation.
- Includes Incident ID, Machine Name, CPU %, Memory, ThreadPool status, SQL status, SignalR status, and directory path.
- Attaches `Summary.json`. Does NOT attach dump files.
- Fail-safe: handles SMTP errors gracefully without affecting application execution.

### 5. Browser Telemetry & Heartbeat (`freeze-monitor.js`)
- Included globally in layout (`_Layout.cshtml`).
- Tracks JS unhandled errors, promise rejections, online/offline state, visibility changes, fetch/AJAX 5xx failures, and SignalR state.
- Periodic heartbeat probe every 15s (`GET /diagnostics/heartbeat`).
- Posts diagnostic payload to `POST /diagnostics/browser` upon repeated client failures.

### 6. Real-Time Diagnostics Dashboard (`/Admin/Diagnostics`)
- Manager-protected Razor View (`/Admin/Diagnostics`).
- High-tech dark theme displaying CPU, Memory, ThreadPool, SQL latency, SignalR connections, pending requests, uptime, and incident history.
- 5-second automatic UI refresh cycle.
- "Capture Incident Now" manual trigger button.

### 7. Incident History & Manual Capture APIs
- `GET /diagnostics/incidents` -> Returns incident history list.
- `GET /diagnostics/incidents/{id}` -> Returns specific incident details.
- `POST /diagnostics/incident/create` -> Triggers manual incident capture.
- `GET /diagnostics/heartbeat` -> Lightweight heartbeat probe endpoint.

---

## Configuration Reference

Appsettings configuration block:

```json
{
  "FreezeMonitor": {
    "Enabled": true,
    "SamplingIntervalSeconds": 10,
    "SqlHealthIntervalSeconds": 30,
    "SqlHealthTimeoutSeconds": 5,
    "RingBufferSize": 500,
    "LongRequestThresholdSeconds": 5,
    "MaxLongRequests": 50,
    "IncidentFolder": "Incidents",
    "EnableDiagnosticsApi": true,
    "FreezeDetection": {
      "Enabled": true,
      "NoCompletedRequestSeconds": 30,
      "PendingRequestThreshold": 20,
      "ThreadPoolWorkerThreshold": 5,
      "SqlFailureThreshold": 3,
      "HeartbeatFailureThreshold": 3
    }
  },
  "SMTP": {
    "Enabled": true,
    "Host": "smtp.example.com",
    "Port": 587,
    "UseSSL": true,
    "Username": "alerts@example.com",
    "Password": "SecretPassword",
    "FromEmail": "freezemonitor@example.com",
    "FromName": "Freeze Monitor Alert",
    "ToEmails": [
      "sre-team@example.com"
    ]
  },
  "ProcDump": {
    "Enabled": false,
    "ExecutablePath": "C:\\Tools\\ProcDump\\procdump.exe",
    "Arguments": "-ma {PID} {OUTPUT}",
    "TimeoutSeconds": 120
  }
}
```

---

## Security & Access Control

- All `/diagnostics/*` endpoints and `/Admin/Diagnostics` dashboard require an authenticated user in the `Manager` role (`[Authorize(Roles = "Manager")]`).
- POST endpoints require CSRF anti-forgery validation.
- Client telemetry excludes personal data, chat content, and request bodies.

---

## Troubleshooting

1. **ProcDump Not Generating Dumps**:
   - Check if ProcDump path is correct and accessible by the IIS AppPool user.
   - Verify `ProcDump:Enabled` is set to `true`.
   - Ensure the process has permissions to execute external processes.

2. **SMTP Email Alerts Failing**:
   - Verify SMTP Host, Port, SSL, and credentials.
   - Check application log for category `FreezeMonitor` error entries.

3. **Dashboard Access Denied (403/401)**:
   - Ensure the logged-in user has the `Manager` role assigned in ASP.NET Core Identity.
