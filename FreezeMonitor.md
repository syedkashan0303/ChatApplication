# Freeze Monitor

## Purpose

The Freeze Monitor is an isolated, in-process diagnostics module for the ASP.NET Core application. It records lightweight health samples and request/SignalR telemetry so a future production freeze can be correlated with application, SQL, and connection state. It does not change chat messages, room membership, authentication, controllers, or database schema.

## Architecture

```text
HTTP request -> RequestMonitorMiddleware -> RequestStatisticsService
                                             |
SignalR connect/activity/disconnect -> SignalRHealthService
                                             |
FreezeMonitorService (PeriodicTimer) -> SystemHealthCollector -> RingBufferService
                                  |          |       |       |
                                  |          |       |       +-> ThreadPool/process/GC metrics
                                  |          |       +-> SignalR snapshot
                                  |          +-> latest SQL SELECT 1 snapshot
                                  +-> SqlHealthChecker (configured interval)

DiagnosticsController -> RingBufferService / statistics / SignalR / latest incident
IncidentWriterService -> formatted JSON files only when explicitly called
```

## Flow

1. `AddFreezeMonitor()` registers the module as singleton services and starts the sampler when `FreezeMonitor:Enabled` is true at application startup.
2. The sampler immediately runs, then records a health snapshot at `SamplingIntervalSeconds`.
3. SQL health uses only `SELECT 1` through a new connection and runs at `SqlHealthIntervalSeconds`; it never executes a business query.
4. Snapshots remain in the fixed-size ring buffer. They are not written to disk on every sample.
5. The request middleware records normal HTTP request lifetime and produces warnings only for requests above the configured threshold. WebSocket requests are excluded because SignalR connection lifetime is measured separately; treating a healthy persistent WebSocket as a long HTTP request would create false alerts.
6. SignalR hub lifecycle and hub invocations update an independent connection registry. No message, group, or authorization logic is changed.
7. An incident JSON bundle is written only when another trusted component calls `IncidentWriterService.WriteIncidentAsync(...)`. Automatic freeze classification belongs to Phase 3.

## Configuration

The section is in `appsettings.json`:

```json
"FreezeMonitor": {
  "Enabled": true,
  "SamplingIntervalSeconds": 10,
  "SqlHealthIntervalSeconds": 30,
  "SqlHealthTimeoutSeconds": 5,
  "RingBufferSize": 500,
  "LongRequestThresholdSeconds": 5,
  "MaxLongRequests": 50,
  "IncidentFolder": "Incidents",
  "EnableDiagnosticsApi": true
}
```

- `Enabled`: starts the hosted sampler at application startup and enables request measurement. Set to `false` and restart to disable collection.
- `SamplingIntervalSeconds`: health-snapshot interval. Minimum `1`.
- `SqlHealthIntervalSeconds`: `SELECT 1` interval. Minimum `1`.
- `SqlHealthTimeoutSeconds`: cancellation/command timeout for the health probe. Minimum `1`.
- `RingBufferSize`: newest snapshots retained in memory. Valid range `1` through `10000`.
- `LongRequestThresholdSeconds`: warning and active-long-request threshold. Minimum `1`.
- `MaxLongRequests`: maximum active long requests returned by diagnostics. Valid range `1` through `1000`.
- `IncidentFolder`: relative to the application content root unless an absolute path is configured.
- `EnableDiagnosticsApi`: controls diagnostics API availability while monitoring remains active.

Options are validated during startup. The sampling hosted service is registered from the startup value of `Enabled`; changing that setting requires an application restart. Request thresholds and SQL timeout/interval values are read from the current configuration on subsequent collection cycles.

## Services

- `FreezeMonitorService`: background sampler using `PeriodicTimer`.
- `SystemHealthCollector`: combines process, CPU, memory, GC, ThreadPool, SQL, SignalR, and request data into `HealthSnapshot`.
- `SqlHealthChecker`: async, timeout-bounded `SELECT 1` probe with separate connection/execution timings.
- `CpuUsageService`: calculates process CPU percentage from successive process-time samples.
- `SignalRHealthService`: thread-safe active-connection registry and daily/peak connection metrics.
- `RequestStatisticsService`: thread-safe active request registry, cumulative response metrics, failures, rate, and active long requests.
- `RingBufferService`: lock-protected fixed-capacity circular buffer; only the newest snapshots remain.
- `IncidentWriterService`: creates formatted incident JSON files on demand.
- `RequestMonitorMiddleware`: records ordinary HTTP request timing before the rest of the application pipeline.

All module logs are created with the logger category `FreezeMonitor`, which Serilog stores as `SourceContext = FreezeMonitor`.

## Diagnostics Endpoints

All endpoints require an authenticated user in the `Manager` role. They return `404` when the monitor or diagnostics API is disabled.

| Endpoint | Result |
| --- | --- |
| `GET /diagnostics/health` | Application information, latest health sample, request statistics, and current SignalR snapshot. |
| `GET /diagnostics/statistics` | Current/pending, completed, failed, average/longest, per-minute, and active long request statistics. |
| `GET /diagnostics/ringbuffer` | Up to the newest 50 health samples, oldest to newest. |
| `GET /diagnostics/incident/latest` | Latest in-memory or persisted incident summary; returns `404` if no incident exists. |

## Incident Files

Calling the incident writer creates a directory such as:

```text
Incidents/
  2026-07-28_14-22-15/
    Health.json
    Statistics.json
    SignalR.json
    ThreadPool.json
    SQL.json
    Application.json
    Summary.json
```

Files are formatted JSON. The writer logs at `Critical` after a successful bundle creation and at `Error` if file creation fails. It does not write normal 10-second samples to disk.

## Performance Considerations

- The ring buffer has fixed capacity and retains references only to the configured number of snapshots.
- SQL checks are a single async `SELECT 1` and have an independent timeout.
- Shared request and SignalR state use `ConcurrentDictionary`, `ConcurrentQueue`, `Interlocked`, and narrow locks for fixed-size/daily counters.
- No business query, schema change, timer per request, synchronous wait, or periodic disk write is used.
- CPU calculation and process metrics are sampled only on the configured background interval.
- The monitor is process-local. In a web-farm deployment, each worker process needs its own monitoring and centralized log/incident collection for cross-instance correlation.

## Enable and Disable

Set `FreezeMonitor:Enabled` to `true` and restart the application to enable the background sampler and request monitoring. Set it to `false` and restart to disable them. `EnableDiagnosticsApi` can independently hide the diagnostic endpoints while collection remains enabled.

## Phase 3 Direction

Phase 3 can add evidence-based automatic incident triggers, counter threshold policies, process dump/ProcDump integration with explicit operational approval, browser correlation IDs, a diagnostics dashboard, and centralized multi-instance incident storage. Those additions can call the existing `IncidentWriterService` without redesigning Phase 1 or Phase 2.
