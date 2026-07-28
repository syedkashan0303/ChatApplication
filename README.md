# Chat Application (ASP.NET Core MVC + SignalR)

This repository contains a real-time chat application built with ASP.NET Core 8, MVC, Razor Pages, Entity Framework Core, SQL Server, and SignalR. The project is a hybrid web application that uses Identity for authentication, controllers for HTTP endpoints, and a custom SignalR hub for live messaging.

> This documentation is based on the current source code in the repository. Where a feature is not clearly implemented, that section explicitly notes that no evidence was found.

---

## 1. Project Overview

The application provides:
- Group chat rooms backed by database records
- One-to-one chat between users
- Real-time message delivery with SignalR
- Unread message tracking for both room and personal chats
- Admin-only management screens for groups and users
- Login audit logging
- Custom middleware and background services for monitoring and cleanup

### Runtime stack
- ASP.NET Core 8 MVC
- Razor Pages for Identity UI
- SignalR hub for live chat
- Entity Framework Core with SQL Server
- ASP.NET Core Identity
- Serilog for file-based logging
- Bootstrap + jQuery + toastr for the main chat UI

---

## 2. Architecture at a Glance

### Main application entry point
- Startup logic is in [Program.cs](Program.cs)
- The app registers:
  - `AppDbContext`
  - Identity services
  - MVC + Razor Pages
  - SignalR
  - custom middleware and background services

### Main real-time chat hub
- The hub implementation is in [BasicChatHub.cs](BasicChatHub.cs)
- It handles:
  - connection lifecycle
  - room joins and leave operations
  - group message sending
  - private message sending
  - edit/delete operations
  - unread count updates
  - read receipt actions

### Main HTTP controllers
- [Controllers/HomeController.cs](Controllers/HomeController.cs)
  - loads room/user lists
  - fetches chat history
  - handles theme toggle and health ping
- [Controllers/GroupController.cs](Controllers/GroupController.cs)
  - admin-only group/user management endpoints and pages

### Data access layer
- [Areas/Identity/Data/AppDbContext.cs](Areas/Identity/Data/AppDbContext.cs)
- This is the central EF Core context and includes all application entity sets.

---

## 3. Authentication and Authorization

The application uses ASP.NET Core Identity.

### Evidence in code
- Identity is configured in [Program.cs](Program.cs)
- `AddDefaultIdentity<ApplicationUser>()`
- `AddRoles<IdentityRole>()`
- `AddEntityFrameworkStores<AppDbContext>()`
- Identity UI pages are enabled via `MapRazorPages()`

### User model
- [Models/ApplicationUser.cs](Models/ApplicationUser.cs)
- Additional fields:
  - `FullName`
  - `IsDeleted`
  - `IsDarkTheme`

### Role-based access control
- The custom filter [CustomClasses/AdminOnlyAttribute.cs](CustomClasses/AdminOnlyAttribute.cs) checks that the user is authenticated and has the `Manager` role.
- This filter protects admin/admin-only actions in the group management controller.

### Notes on external auth
- The code includes external login handling under the Identity area, but the primary working flow appears to be standard login/registration through Identity pages.
- No custom OAuth provider setup was found in the startup config.

---

## 4. Domain Models and Database Design

The EF context defines the following entity sets:
- `ChatMessages`
- `UsersMessage`
- `ChatRoom`
- `GroupUserMapping`
- `EditedtMessagesLogs`
- `ChatLogs`
- `ChatMessageReadStatuses`
- `UsersMessageReadStatus`
- `UserLoginLogs`

### Core models
- [Models/ChatMessage.cs](Models/ChatMessage.cs)
  - used for room/group messages
- [Models/UsersMessage.cs](Models/UsersMessage.cs)
  - used for private messages
- [Models/ChatRoom.cs](Models/ChatRoom.cs)
  - chat room/group records
- [Models/GroupUserMapping.cs](Models/GroupUserMapping.cs)
  - maps users to groups
- [Models/ChatMessageReadStatus.cs](Models/ChatMessageReadStatus.cs)
  - unread tracking for room messages
- `UsersMessageReadStatus`
  - unread tracking for private messages

### Login audit model
- [Models/UserLoginLog.cs](Models/UserLoginLog.cs)
- There is an explicit table mapping for `UserLoginLogs` in the context.

### Database conventions
- SQL Server is used.
- Migrations are present under [Migrations](Migrations)
- The connection string is configured in [appsettings.json](appsettings.json)

---

## 5. Chat Features Implemented

### 5.1 Group chat rooms
The application loads rooms from the database and allows users to join a room by name.

Behavior:
- Messages sent to a room are stored in `ChatMessages`
- A chat room name is used as the group identifier
- The hub sends updates to the group via `Clients.Group(roomName)`
- Read-status records are created for all group members except the sender

### 5.2 Private / one-to-one chat
Private chats are handled separately using `UsersMessage` and `UsersMessageReadStatus`.

Behavior:
- Messages are stored with both `SenderId` and `ReceiverId`
- The hub sends updates to the receiver and sender individually
- Unread count deltas are sent only to the affected recipient

### 5.3 Message editing and deletion
The hub supports:
- `EditMessage`
- `DeleteMessage`

The frontend modal and buttons in [Views/Home/Index.cshtml](Views/Home/Index.cshtml) allow users to edit/delete messages they own.

### 5.4 Unread tracking
Unread handling is implemented with two models:
- `ChatMessageReadStatus` for room chats
- `UsersMessageReadStatus` for private chats

The hub methods include:
- `MarkMessagesAsRead(int roomId)`
- `P_To_P_MarkMessagesAsRead(string userId)`
- `GetUnreadMessageCounts()`
- `BroadcastUnreadCount(...)`

The main UI uses badges in the room list to show unread counts.

### 5.5 Theme support
The controller endpoints `GetTheme` and `UpdateTheme` allow the current user’s dark/light theme preference to be stored and toggled.

---

## 6. Frontend Structure

The main chat page is [Views/Home/Index.cshtml](Views/Home/Index.cshtml).

### What the UI includes
- Sidebar with search and room/user list
- Chat header with room name and font-size selector
- Message list panel with infinite scroll behavior
- Text area for sending messages
- Modal for editing messages
- Toastr notifications

### Client-side behavior
The view uses jQuery and SignalR to:
- connect to `/hubs/basicchat`
- load rooms via `/Home/GetRooms`
- load chat history via `/Home/GetMessagesByRoom`
- update unread badges in real time
- render message bubbles and unread markers
- handle edit/delete actions

### Important frontend note
The UI appears to be built around the main chat screen and not a separate SPA framework.

---

## 7. HTTP Endpoints and API Surface

### Home endpoints
The controller exposes the following actions:

| Endpoint | Method | Purpose |
|---|---:|---|
| `/Home/Index` | GET | Main chat page |
| `/Home/GetMessagesByRoom` | GET | Fetches room or private chat messages |
| `/Home/GetRooms` | GET | Returns rooms and users for sidebar |
| `/Home/GetTheme` | GET | Gets user theme preference |
| `/Home/UpdateTheme` | GET | Toggles user theme preference |
| `/Home/ping` | GET | Health check endpoint |
| `/SendMessageToAll` | GET | Legacy endpoint for broadcast-style message send |
| `/SendMessageToReceiver` | GET | Legacy endpoint for direct send |
| `/SendMessageToGroup` | POST | Legacy endpoint for group send |

### Group management endpoints
The admin controller includes actions for:
- viewing groups
- editing group names inline
- creating groups
- deleting groups
- viewing user lists
- locking users
- changing user roles

### API documentation note
No dedicated OpenAPI/Swagger setup was found in the source code.

---

## 8. SignalR Hub Methods

The hub is mapped at `/hubs/basicchat` in [Program.cs](Program.cs).

Key methods include:
- `JoinRoom(string roomName)`
- `LeaveRoom(string roomName)`
- `SendMessageToRoom(...)`
- `SendMessageToUser(...)`
- `EditMessage(...)`
- `DeleteMessage(...)`
- `MarkMessagesAsRead(int roomId)`
- `P_To_P_MarkMessagesAsRead(string userId)`
- `GetUnreadMessageCounts()`
- `BroadcastUnreadCount(...)`
- `ForceLogout()`

### Client events used by the UI
- `MessageReceived`
- `MessageEdited`
- `MessageDeleted`
- `UserMessageDeleted`
- `SendMessageUser`
- `Error`
- `ReceiveUnreadCount`
- `ReceiveUnreadDelta`
- `RedirectToLogin`

---

## 9. Middleware, Health, and Background Services

### Custom middleware
- [CustomClasses/GlobalExceptionMiddleware.cs](CustomClasses/GlobalExceptionMiddleware.cs)
  - catches unhandled exceptions and writes a JSON error payload/log entry
- [CustomClasses/ResponseTimeMiddleware.cs](CustomClasses/ResponseTimeMiddleware.cs)
  - present in the repo but not actively used in startup

### Health tracking
- [CustomClasses/AppHealthTracker.cs](CustomClasses/AppHealthTracker.cs)
  - tracks recent activity for runtime health checks

### Background jobs
- [CustomClasses/DatabaseJobService.cs](CustomClasses/DatabaseJobService.cs)
  - runs a stored procedure named `DeleteOldReadMappingRecord`
- [CustomClasses/ScheduledTaskService.cs](CustomClasses/ScheduledTaskService.cs)
  - repeatedly invokes the database cleanup job every 2 hours

### Notes on scheduled tasks
- The stored procedure name is referenced in code, but the SQL script itself is not shown in the repo snapshot.
- This should be verified in the target database environment before deployment.

---

## 10. Configuration and Environment Settings

The main configuration file is [appsettings.json](appsettings.json).

### Important settings
- SQL Server connection string: `AppDbContextConnection`
- Application base URL: `AppSettings:BaseUrl`
- Serilog file logging settings under `Serilog`
- Logging severity configuration

### Startup-specific details
- The application stores Data Protection keys under `D:\ChatAppKeys`
- Logs are written to `Logs\log-.txt`

### Note on hardcoded paths
The code contains hardcoded Windows-style paths for:
- Data Protection storage
- exception log output

These may need adjustment for non-Windows deployment environments.

---

## 11. Setup Instructions

### Prerequisites
- .NET 8 SDK
- SQL Server instance
- Visual Studio 2022 or VS Code with C# support

### Steps
1. Clone the repository.
2. Restore NuGet packages.
3. Update the connection string in [appsettings.json](appsettings.json).
4. Create or update the database using EF Core migrations.
5. Run the application.

### Recommended database command
If migrations are not applied automatically in your environment, use:

```bash
dotnet ef database update
```

### Run locally
```bash
dotnet run
```

### Build check
```bash
dotnet build
```

---

## 12. Suggested Testing Flow

1. Register or log in.
2. Open the chat page.
3. Verify that the room list loads correctly.
4. Open a room and send a message.
5. Open another browser/session and confirm the message arrives in real time.
6. Check that unread badges update.
7. Open a private chat and confirm message delivery and unread handling.
8. Use the edit/delete controls to verify ownership rules.

---

## 13. Not Found / Not Evidenced in Source

The following items were requested during review but no clear implementation evidence was found in the repository snapshot:
- File attachments or media upload support
- Voice/video calling
- Push notifications outside the browser SignalR flow
- Swagger/OpenAPI documentation
- Separate REST API controller documentation
- A dedicated admin dashboard beyond the group/user management views

---

## 14. Maintenance Notes

- The project is currently a server-rendered MVC application with embedded JavaScript rather than a fully separated API + frontend architecture.
- SignalR and EF Core are both heavily used; changes to message schema should be reviewed carefully because both room and private chat models are involved.
- The scheduled cleanup job depends on the database stored procedure being present and correctly configured.
- The logging setup writes files, so log rotation and storage path planning are important for production.

---

## 15. WhatsApp-Style Message Replies

Added: 2026-06-23

Group chat replies now use a self-referencing `ChatMessages.ReplyToMessageId` relationship instead of a separate reply thread system.

### How it works

- Clicking Reply on a group message shows a reply preview above the composer.
- Sending a message includes `replyToMessageId` when a reply target is selected.
- Messages with `ReplyToMessageId` render an inline reply preview inside the bubble.
- Clicking the inline preview scrolls to the original message and highlights it briefly.
- Private chat does not support replies.

### Database shape

- `ChatMessages.ReplyToMessageId` is nullable and references `ChatMessages.Id`.
- The old `MessageReplies` table is removed by migration.
- The separate reply count UI and APIs are removed.

### Implementation notes

- Message history now projects reply metadata in the same query that loads chat messages.
- SignalR group sends include the reply metadata so live messages render consistently.
- The reply composer is inline, so there is no modal or separate thread view.

---

## 16. Production Bug Investigation — Thread Pool Starvation

Investigated and fixed: 2026-06-17

### Symptoms

- 8–10 simultaneous users in active group chats.
- Randomly, all users experience a complete application freeze.
- Chat stops updating, AJAX requests receive no response, SignalR messages stop arriving.
- No visible frontend error.
- The freeze lasts several minutes then recovers automatically or after a browser refresh.
- Occurs intermittently and is difficult to reproduce locally.

---

### Root Cause

**Primary cause: synchronous blocking ADO.NET inside a `BackgroundService`, causing ThreadPool starvation.**

The exact causal chain:

**Step 1** — `ScheduledTaskService` fires every 2 hours and calls `DatabaseJobService.RunStoredProcedure()`.

**Step 2** — `RunStoredProcedure()` was a synchronous `void` method using `conn.Open()` and `cmd.ExecuteNonQuery()`. These are blocking synchronous calls that hold a .NET ThreadPool worker thread for the entire duration of the stored procedure.

**Step 3** — The stored procedure `DeleteOldReadMappingRecord` performs a bulk `DELETE` on the `ChatMessageReadStatuses` table, acquiring row-level or page-level SQL locks.

**Step 4** — Active users are simultaneously sending messages (`SendMessageToRoom` inserts to `ChatMessageReadStatuses`) and opening rooms (`MarkMessagesAsRead` deletes from `ChatMessageReadStatuses`). These operations wait on the lock held by the stored procedure.

**Step 5** — Each waiting database operation holds its own ThreadPool thread while waiting for the lock. With 8–10 active users the ThreadPool fills up entirely.

**Step 6** — ThreadPool starvation: no threads are available to process new HTTP requests or SignalR message delivery. All incoming requests queue. AJAX calls time out. SignalR stops delivering messages.

**Step 7** — After 30 seconds the default `SqlCommand.CommandTimeout` fires. `cmd.ExecuteNonQuery()` throws a `SqlException`. Because there was **no `try/catch`** in `ScheduledTaskService.ExecuteAsync`, the exception propagated out of the `while` loop and **permanently terminated the background service**. The cleanup job never ran again until the next application restart.

**Step 8** — With the blocking thread freed, the ThreadPool recovers and the application becomes responsive again.

This explains every observed symptom:

| Symptom | Explanation |
|---|---|
| Intermittent | Fires at 2-hour intervals; worse under active load |
| All users affected simultaneously | ThreadPool starvation is process-wide |
| Several minutes freeze | 30-second command timeout + ThreadPool drain time |
| No frontend error | Requests are queued, not rejected with an error code |
| Automatic recovery | After `CommandTimeout` fires, threads free up |
| Hard to reproduce locally | Requires concurrent load + exact 2-hour timing |

---

### Secondary issues found

| # | Location | Issue | Severity |
|---|---|---|---|
| 1 | [CustomClasses/ScheduledTaskService.cs](CustomClasses/ScheduledTaskService.cs) | No `try/catch` around `RunStoredProcedure()` — one SP failure permanently killed the `while` loop and stopped all future cleanup runs until app restart | High |
| 2 | [CustomClasses/DatabaseJobService.cs](CustomClasses/DatabaseJobService.cs) | `void` method using synchronous `conn.Open()` and `cmd.ExecuteNonQuery()` — blocks a ThreadPool thread for the full SP duration | Critical |
| 3 | [CustomClasses/DatabaseJobService.cs](CustomClasses/DatabaseJobService.cs) | No `CommandTimeout` set on `SqlCommand` — the default 30 seconds is the only thing that eventually frees the thread | High |
| 4 | [CustomClasses/DatabaseJobService.cs](CustomClasses/DatabaseJobService.cs) | No logging — impossible to know in Serilog output when the SP started, finished, or how long it took | Medium |
| 5 | [CustomClasses/LoggingHubFilter.cs](CustomClasses/LoggingHubFilter.cs) | All hub calls logged at `Information` level with no slow-method threshold — slow operations during a freeze are indistinguishable from normal calls in logs | Medium |
| 6 | [CustomClasses/AppHealthTracker.cs](CustomClasses/AppHealthTracker.cs) | No active connection count — the Ping endpoint had no visibility into how many SignalR clients were connected | Medium |
| 7 | [Controllers/HomeController.cs](Controllers/HomeController.cs) | `GET /Home/ping` returned only idle time as plain text — no ThreadPool or connection data to detect starvation remotely | Medium |
| 8 | [BasicChatHub.cs](BasicChatHub.cs) | `OnConnectedAsync` and `OnDisconnectedAsync` did not log `UserId` or `ConnectionId` — impossible to trace which user was connected during a freeze | Low |

---

### Files changed

| File | What changed |
|---|---|
| [CustomClasses/DatabaseJobService.cs](CustomClasses/DatabaseJobService.cs) | `void RunStoredProcedure()` replaced with `async Task RunStoredProcedureAsync()` using `OpenAsync()` and `ExecuteNonQueryAsync()`. Added explicit `CommandTimeout = 120`. Added `ILogger<DatabaseJobService>` with start/finish/error timing. |
| [CustomClasses/ScheduledTaskService.cs](CustomClasses/ScheduledTaskService.cs) | Calls `await RunStoredProcedureAsync()` instead of the synchronous version. Wrapped in `try/catch` so a failure logs the error and retries after 2 hours instead of permanently terminating the service. Added start/stop/error logging. |
| [CustomClasses/LoggingHubFilter.cs](CustomClasses/LoggingHubFilter.cs) | Added 2-second slow-method threshold. Methods exceeding it are logged at `Warning` level with `ConnectionId`. Normal calls remain at `Information`. |
| [CustomClasses/AppHealthTracker.cs](CustomClasses/AppHealthTracker.cs) | Added thread-safe `ActiveConnections` counter using `Interlocked.Increment` / `Interlocked.Decrement`. |
| [BasicChatHub.cs](BasicChatHub.cs) | `OnConnectedAsync` calls `AppHealthTracker.TrackConnect()` and logs `UserId` + `ConnectionId` + current connection count. `OnDisconnectedAsync` calls `AppHealthTracker.TrackDisconnect()` and logs clean vs error disconnect reason. |
| [Controllers/HomeController.cs](Controllers/HomeController.cs) | `GET /Home/ping` now returns a JSON object with `status`, `idleSeconds`, `activeSignalRConnections`, and a `threadPool` block containing `workerAvailable`, `workerInUse`, `workerMax`, `iocpAvailable`, `iocpInUse`, `iocpMax`. |

---

### What the Ping endpoint now returns

`GET /Home/ping` — sample healthy response:

```json
{
  "status": "healthy",
  "idleSeconds": 3,
  "activeSignalRConnections": 9,
  "threadPool": {
    "workerAvailable": 32755,
    "workerInUse": 5,
    "workerMax": 32767,
    "workerMin": 8,
    "iocpAvailable": 1000,
    "iocpInUse": 0,
    "iocpMax": 1000,
    "iocpMin": 8
  },
  "timestamp": "2026-06-17T14:22:10Z"
}
```

During a ThreadPool starvation event `workerInUse` will be close to `workerMax` and the response itself will be delayed or will not arrive.

---

### SQL scripts for production investigation

Run these in SSMS to diagnose the issue if it recurs.

**1 — Active blocking chains**
```sql
SELECT
    blocking.session_id  AS blocking_session,
    blocked.session_id   AS blocked_session,
    blocked.wait_type,
    blocked.wait_time / 1000.0 AS wait_seconds,
    sq_blocked.text      AS blocked_sql,
    sq_blocking.text     AS blocking_sql
FROM sys.dm_exec_sessions blocked
JOIN sys.dm_exec_sessions blocking
    ON blocked.blocking_session_id = blocking.session_id
CROSS APPLY sys.dm_exec_sql_text(blocked.most_recent_sql_handle)  sq_blocked
CROSS APPLY sys.dm_exec_sql_text(blocking.most_recent_sql_handle) sq_blocking
ORDER BY blocked.wait_time DESC;
```

**2 — Long-running queries right now**
```sql
SELECT
    r.session_id,
    r.status,
    r.wait_type,
    r.wait_time / 1000.0          AS wait_seconds,
    r.total_elapsed_time / 1000.0 AS elapsed_seconds,
    t.text                        AS sql_text,
    s.login_name
FROM sys.dm_exec_requests r
JOIN sys.dm_exec_sessions s ON r.session_id = s.session_id
CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) t
WHERE r.session_id <> @@SPID
ORDER BY r.total_elapsed_time DESC;
```

**3 — Lock contention on ChatMessageReadStatuses**
```sql
SELECT
    tl.request_session_id,
    tl.resource_type,
    tl.resource_description,
    tl.request_mode,
    tl.request_status,
    t.text AS sql_text
FROM sys.dm_tran_locks tl
JOIN sys.dm_exec_requests r
    ON tl.request_session_id = r.session_id
CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) t
WHERE tl.resource_database_id = DB_ID()
  AND tl.request_status = 'WAIT'
ORDER BY tl.request_session_id;
```

**4 — Connection pool usage by login**
```sql
SELECT
    login_name,
    COUNT(*)                                                  AS connection_count,
    SUM(CASE WHEN status = 'running'  THEN 1 ELSE 0 END)     AS active,
    SUM(CASE WHEN status = 'sleeping' THEN 1 ELSE 0 END)     AS idle
FROM sys.dm_exec_sessions
WHERE is_user_process = 1
GROUP BY login_name
ORDER BY connection_count DESC;
```

---

### Verification steps after deployment

1. Deploy the updated build.

---

## 16. Production Bug Investigation — Thread Pool Starvation

Investigated and fixed: 2026-06-17

### Symptoms

- 8–10 simultaneous users in active group chats.
- Randomly, all users experience a complete application freeze.
- Chat stops updating, AJAX requests receive no response, SignalR messages stop arriving.
- No visible frontend error.
- The freeze lasts several minutes then recovers automatically or after a browser refresh.
- Occurs intermittently and is difficult to reproduce locally.

---

### Root Cause

**Primary cause: synchronous blocking ADO.NET inside a `BackgroundService`, causing ThreadPool starvation.**

The exact causal chain:

**Step 1** — `ScheduledTaskService` fires every 2 hours and calls `DatabaseJobService.RunStoredProcedure()`.

**Step 2** — `RunStoredProcedure()` was a synchronous `void` method using `conn.Open()` and `cmd.ExecuteNonQuery()`. These are blocking synchronous calls that hold a .NET ThreadPool worker thread for the entire duration of the stored procedure.

**Step 3** — The stored procedure `DeleteOldReadMappingRecord` performs a bulk `DELETE` on the `ChatMessageReadStatuses` table, acquiring row-level or page-level SQL locks.

**Step 4** — Active users are simultaneously sending messages (`SendMessageToRoom` inserts to `ChatMessageReadStatuses`) and opening rooms (`MarkMessagesAsRead` deletes from `ChatMessageReadStatuses`). These operations wait on the lock held by the stored procedure.

**Step 5** — Each waiting database operation holds its own ThreadPool thread while waiting for the lock. With 8–10 active users the ThreadPool fills up entirely.

**Step 6** — ThreadPool starvation: no threads are available to process new HTTP requests or SignalR message delivery. All incoming requests queue. AJAX calls time out. SignalR stops delivering messages.

**Step 7** — After 30 seconds the default `SqlCommand.CommandTimeout` fires. `cmd.ExecuteNonQuery()` throws a `SqlException`. Because there was **no `try/catch`** in `ScheduledTaskService.ExecuteAsync`, the exception propagated out of the `while` loop and **permanently terminated the background service**. The cleanup job never ran again until the next application restart.

**Step 8** — With the blocking thread freed, the ThreadPool recovers and the application becomes responsive again.

This explains every observed symptom:

| Symptom | Explanation |
|---|---|
| Intermittent | Fires at 2-hour intervals; worse under active load |
| All users affected simultaneously | ThreadPool starvation is process-wide |
| Several minutes freeze | 30-second command timeout + ThreadPool drain time |
| No frontend error | Requests are queued, not rejected with an error code |
| Automatic recovery | After `CommandTimeout` fires, threads free up |
| Hard to reproduce locally | Requires concurrent load + exact 2-hour timing |

---

### Secondary issues found

| # | Location | Issue | Severity |
|---|---|---|---|
| 1 | [CustomClasses/ScheduledTaskService.cs](CustomClasses/ScheduledTaskService.cs) | No `try/catch` around `RunStoredProcedure()` — one SP failure permanently killed the `while` loop and stopped all future cleanup runs until app restart | High |
| 2 | [CustomClasses/DatabaseJobService.cs](CustomClasses/DatabaseJobService.cs) | `void` method using synchronous `conn.Open()` and `cmd.ExecuteNonQuery()` — blocks a ThreadPool thread for the full SP duration | Critical |
| 3 | [CustomClasses/DatabaseJobService.cs](CustomClasses/DatabaseJobService.cs) | No `CommandTimeout` set on `SqlCommand` — the default 30 seconds is the only thing that eventually frees the thread | High |
| 4 | [CustomClasses/DatabaseJobService.cs](CustomClasses/DatabaseJobService.cs) | No logging — impossible to know in Serilog output when the SP started, finished, or how long it took | Medium |
| 5 | [CustomClasses/LoggingHubFilter.cs](CustomClasses/LoggingHubFilter.cs) | All hub calls logged at `Information` level with no slow-method threshold — slow operations during a freeze are indistinguishable from normal calls in logs | Medium |
| 6 | [CustomClasses/AppHealthTracker.cs](CustomClasses/AppHealthTracker.cs) | No active connection count — the Ping endpoint had no visibility into how many SignalR clients were connected | Medium |
| 7 | [Controllers/HomeController.cs](Controllers/HomeController.cs) | `GET /Home/ping` returned only idle time as plain text — no ThreadPool or connection data to detect starvation remotely | Medium |
| 8 | [BasicChatHub.cs](BasicChatHub.cs) | `OnConnectedAsync` and `OnDisconnectedAsync` did not log `UserId` or `ConnectionId` — impossible to trace which user was connected during a freeze | Low |

---

### Files changed

| File | What changed |
|---|---|
| [CustomClasses/DatabaseJobService.cs](CustomClasses/DatabaseJobService.cs) | `void RunStoredProcedure()` replaced with `async Task RunStoredProcedureAsync()` using `OpenAsync()` and `ExecuteNonQueryAsync()`. Added explicit `CommandTimeout = 120`. Added `ILogger<DatabaseJobService>` with start/finish/error timing. |
| [CustomClasses/ScheduledTaskService.cs](CustomClasses/ScheduledTaskService.cs) | Calls `await RunStoredProcedureAsync()` instead of the synchronous version. Wrapped in `try/catch` so a failure logs the error and retries after 2 hours instead of permanently terminating the service. Added start/stop/error logging. |
| [CustomClasses/LoggingHubFilter.cs](CustomClasses/LoggingHubFilter.cs) | Added 2-second slow-method threshold. Methods exceeding it are logged at `Warning` level with `ConnectionId`. Normal calls remain at `Information`. |
| [CustomClasses/AppHealthTracker.cs](CustomClasses/AppHealthTracker.cs) | Added thread-safe `ActiveConnections` counter using `Interlocked.Increment` / `Interlocked.Decrement`. |
| [BasicChatHub.cs](BasicChatHub.cs) | `OnConnectedAsync` calls `AppHealthTracker.TrackConnect()` and logs `UserId` + `ConnectionId` + current connection count. `OnDisconnectedAsync` calls `AppHealthTracker.TrackDisconnect()` and logs clean vs error disconnect reason. |
| [Controllers/HomeController.cs](Controllers/HomeController.cs) | `GET /Home/ping` now returns a JSON object with `status`, `idleSeconds`, `activeSignalRConnections`, and a `threadPool` block containing `workerAvailable`, `workerInUse`, `workerMax`, `iocpAvailable`, `iocpInUse`, `iocpMax`. |

---

### What the Ping endpoint now returns

`GET /Home/ping` — sample healthy response:

```json
{
  "status": "healthy",
  "idleSeconds": 3,
  "activeSignalRConnections": 9,
  "threadPool": {
    "workerAvailable": 32755,
    "workerInUse": 5,
    "workerMax": 32767,
    "workerMin": 8,
    "iocpAvailable": 1000,
    "iocpInUse": 0,
    "iocpMax": 1000,
    "iocpMin": 8
  },
  "timestamp": "2026-06-17T14:22:10Z"
}
```

During a ThreadPool starvation event `workerInUse` will be close to `workerMax` and the response itself will be delayed or will not arrive.

---

### SQL scripts for production investigation

Run these in SSMS to diagnose the issue if it recurs.

**1 — Active blocking chains**
```sql
SELECT
    blocking.session_id  AS blocking_session,
    blocked.session_id   AS blocked_session,
    blocked.wait_type,
    blocked.wait_time / 1000.0 AS wait_seconds,
    sq_blocked.text      AS blocked_sql,
    sq_blocking.text     AS blocking_sql
FROM sys.dm_exec_sessions blocked
JOIN sys.dm_exec_sessions blocking
    ON blocked.blocking_session_id = blocking.session_id
CROSS APPLY sys.dm_exec_sql_text(blocked.most_recent_sql_handle)  sq_blocked
CROSS APPLY sys.dm_exec_sql_text(blocking.most_recent_sql_handle) sq_blocking
ORDER BY blocked.wait_time DESC;
```

**2 — Long-running queries right now**
```sql
SELECT
    r.session_id,
    r.status,
    r.wait_type,
    r.wait_time / 1000.0          AS wait_seconds,
    r.total_elapsed_time / 1000.0 AS elapsed_seconds,
    t.text                        AS sql_text,
    s.login_name
FROM sys.dm_exec_requests r
JOIN sys.dm_exec_sessions s ON r.session_id = s.session_id
CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) t
WHERE r.session_id <> @@SPID
ORDER BY r.total_elapsed_time DESC;
```

**3 — Lock contention on ChatMessageReadStatuses**
```sql
SELECT
    tl.request_session_id,
    tl.resource_type,
    tl.resource_description,
    tl.request_mode,
    tl.request_status,
    t.text AS sql_text
FROM sys.dm_tran_locks tl
JOIN sys.dm_exec_requests r
    ON tl.request_session_id = r.session_id
CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) t
WHERE tl.resource_database_id = DB_ID()
  AND tl.request_status = 'WAIT'
ORDER BY tl.request_session_id;
```

**4 — Connection pool usage by login**
```sql
SELECT
    login_name,
    COUNT(*)                                                  AS connection_count,
    SUM(CASE WHEN status = 'running'  THEN 1 ELSE 0 END)     AS active,
    SUM(CASE WHEN status = 'sleeping' THEN 1 ELSE 0 END)     AS idle
FROM sys.dm_exec_sessions
WHERE is_user_process = 1
GROUP BY login_name
ORDER BY connection_count DESC;
```

---

### Verification steps after deployment

1. Deploy the updated build.
2. Watch Serilog output for the first scheduled run at the 2-hour mark. Expect to see:
   - `Starting scheduled cleanup job.`
   - `DeleteOldReadMappingRecord completed in Xms`
   - `Scheduled cleanup job finished successfully.`
3. Poll `GET /Home/ping` during and after the SP execution. `threadPool.workerInUse` should remain low (single digits) throughout.
4. Confirm the service continues to log cleanup attempts every 2 hours without stopping — this confirms the `try/catch` fix is working.
5. If a failure occurs, Serilog will log `Scheduled cleanup job failed. Will retry after 2 hours.` and the service will continue — previously the service would silently die with no log entry after the first failure.

---

## 17. Production Chat Freeze Root Cause Analysis & Fixes

Investigated and fixed: 2026-07-28

### Problem Statement
In production, after running normally for some time, the chat application freezes intermittently:
- UI becomes unresponsive
- Sending and receiving messages stops
- SignalR communication halts with no visible frontend exception
- Refreshing the page temporarily fixes the issue

---

### Root Cause Analysis Summary

1. **Authentication Cookie Hard Expiry (Primary Cause - 35%)**:
   - `ExpireTimeSpan` was set to 60 minutes with `SlidingExpiration = false`.
   - After exactly 60 minutes, the cookie ticket expired. SignalR Hub calls failed `GetUserId()` authentication checks silently.
   - SignalR redirect events were returning 302/HTML redirects instead of `401 Unauthorized`.
2. **IIS Application Pool Idle Shutdown (20%)**:
   - Default IIS App Pool `Idle Timeout` of 20 minutes shuts down the worker process when HTTP traffic pauses. WebSocket traffic alone doesn't prevent idle shutdown on default IIS settings.
3. **Single-Attempt SignalR Reconnect (10%)**:
   - On connection drop, `connectionChat.onclose` attempted to reconnect only **once** after a fixed 5-second delay. If that attempt failed, it stopped retrying, leaving the UI permanently dead without notifying the user.
4. **Uncaught DB Exceptions in Hub Methods (5%)**:
   - Methods like `MarkMessagesAsRead` lacked `try-catch` blocks. Unhandled DB errors terminated the Hub connection context.
5. **Potential Double Disposal**:
   - `HomeController.GetMessagesByRoom` manually invoked `_db.Dispose()` inside a `finally` block while using scoped injection via `using var scope`.

---

### Status of Fixes

#### ✅ Completed Code Fixes (In Codebase)

| # | Component | File | Changes Implemented |
|---|---|---|---|
| 1 | **Authentication Cookie** | [Program.cs](Program.cs) | Extended `ExpireTimeSpan` to 8 hours. Enabled `SlidingExpiration = true`. Configured `OnRedirectToLogin` to return `401 Unauthorized` for `/hubs` paths instead of HTTP redirects. |
| 2 | **Resilient SignalR Reconnect** | [Views/Home/Index.cshtml](Views/Home/Index.cshtml) | Replaced fixed 5s single retry in `onclose` with exponential backoff algorithm (`retryConnect`, up to 20 attempts, max 60s delay). |
| 3 | **UI Connection Status Banner** | [Views/Home/Index.cshtml](Views/Home/Index.cshtml) | Added `showConnectionBanner()` / `hideConnectionBanner()` to display real-time visual alerts ("Reconnecting...", "Connection lost", "Unable to reconnect") to users. |
| 4 | **Hub Exception Safeguards** | [BasicChatHub.cs](BasicChatHub.cs) | Wrapped `MarkMessagesAsRead` and `P_To_P_MarkMessagesAsRead` in `try-catch` blocks with error logging to prevent socket termination on DB exceptions. |
| 5 | **Authentication Diagnostics** | [BasicChatHub.cs](BasicChatHub.cs) | Added diagnostic logging (`_logger.LogWarning`) in `GetUserId()` to record `ConnId`, `IsAuthenticated`, and transport details whenever auth fails. |
| 6 | **Double-Disposal Cleanup** | [Controllers/HomeController.cs](Controllers/HomeController.cs) | Removed redundant manual `_db.Dispose()` from `finally` block in `GetMessagesByRoom`. |
| 7 | **Script Deduplication** | [Views/Home/Index.cshtml](Views/Home/Index.cshtml) | Removed duplicate jQuery library scripts to prevent script re-initialization side effects. |

---

#### ⏳ Remaining Tasks (IIS & Production Infrastructure Administration)

The following server-level configuration changes must be applied on the IIS Production Host by the system administrator:

1. **IIS Application Pool Settings**:
   - Open IIS Manager → Application Pools → Select App Pool → Advanced Settings.
   - Set **Idle Time-out (minutes)** to `0` (Disabled).
   - Set **Start Mode** to `AlwaysRunning`.
   - Set **Preload Enabled** to `True` on the website.
2. **IIS WebSocket Protocol Feature**:
   - Ensure `Web-WebSockets` feature is installed on Windows Server (`Install-WindowsFeature Web-Sockets`).
   - Confirm `<webSocket enabled="true" />` is configured under `<system.webServer>` in IIS `web.config`.
3. **Data Protection Key Storage Permissions**:
   - Ensure the IIS Application Pool Identity has full Read/Write permissions to `D:\ChatAppKeys`.

---

## 18. Production Freeze Investigation - Evidence-Based Status (2026-07-28)

### Executive Summary

**Root Cause cannot yet be proven from available evidence.**

This review covered the application source, the repository's `Logs` files, SQL archive script, configuration, and build output. It did not include an incident-time browser trace, IIS configuration/logs, Windows Event Viewer data, SQL Server activity snapshots, process dumps, or production machine telemetry. Those missing records are required to distinguish a browser stall, a broken SignalR transport, an IIS worker-process recycle, ASP.NET Core resource exhaustion, or SQL blocking.

The previous section's percentage allocations and primary-cause statement are not supported by the evidence currently in this repository. The current source has already changed relative to several statements in that section: it configures an 8-hour sliding cookie, SignalR automatic reconnect plus a 20-attempt retry loop, async database-job execution, and a Quartz daily trigger. This section supersedes no code and records only what can be established today.

One concrete anomaly was found in the available application logs: Quartz startup is recorded five times on 2026-07-27 at 11:24:28, 11:39:31, 11:48:01, 11:51:49, and 11:55:51. Since Quartz is registered during host startup in `Program.cs`, this is evidence that the host was initialized repeatedly. The logs contain no matching application-stopping entry, IIS/WAS event, deployment record, process ID, environment name, or build version. Therefore the cause of those starts, their relation to the reported production freeze, and even the deployment identity that produced the local log files cannot be established from this repository.

### Scope and Evidence Sources

Reviewed:

- Runtime composition in `Program.cs`, including middleware, authentication, SignalR, Serilog, data protection, EF Core, and Quartz.
- `BasicChatHub.cs`, all custom services/middleware, controllers, EF Core model/migrations, `Views/Home/Index.cshtml`, and the SQL archive procedure.
- Repository log files through 2026-07-27 and the source configuration. No IIS `web.config`, IIS logs, FREB logs, Windows event logs, SQL snapshots, browser exports, or production deployment manifest is present.
- `dotnet build SignalRMVC.sln --no-restore` completed successfully with 0 errors and 64 nullable/style warnings. This establishes compilation only; it does not reproduce or disprove the incident.

### Architecture Diagram

```text
Browser page: Views/Home/Index.cshtml
  |
  +-- AJAX: /Home/GetRooms, /Home/GetMessagesByRoom, etc.
  |
  +-- SignalR JavaScript client
        |
        +-- POST /hubs/basicchat/negotiate
        +-- negotiated SignalR transport (transport is not recorded in available logs)
                |
                v
             IIS / ASP.NET Core Module (IIS configuration is not in repository)
                |
                v
             ASP.NET Core pipeline
             GlobalExceptionMiddleware -> HTTPS -> static files -> Serilog request log
             -> routing -> authentication -> authorization
                |
                +-- BasicChatHub (/hubs/basicchat)
                |     -> per-invocation service scope -> AppDbContext / Identity
                |
                +-- HomeController HTTP endpoints
                      -> per-action service scope -> AppDbContext / SQL Server

Quartz DailyDatabaseJob
  -> DatabaseJobService
  -> SQL stored procedure sp_ArchiveOldChatData
```

Communication can stop at the browser main thread, browser network stack, SignalR transport, IIS/ANCM worker process, ASP.NET Core request execution, authentication, hub/database execution, or SQL Server. The available records do not identify which boundary failed during a reported freeze.

### Runtime Timeline

1. The browser creates exactly one `HubConnection` in `Views/Home/Index.cshtml` and starts it once during page initialization.
2. The client negotiates `/hubs/basicchat`; available log records show successful negotiate requests and `HTTP CONNECT /hubs/basicchat` sessions.
3. `BasicChatHub.OnConnectedAsync` obtains the authenticated user ID, logs the connection, joins the role group and active mapped groups, then completes the hub lifecycle callback.
4. The browser obtains rooms over HTTP and unread counts through `GetUnreadMessageCounts`. Room selection invokes `JoinRoom`, loads messages over HTTP, and invokes the appropriate mark-as-read method.
5. Sending a message creates a scoped `AppDbContext`, persists the message/read-status rows, then broadcasts a SignalR event. The daily archive job runs independently through Quartz and `sp_ArchiveOldChatData`.
6. SignalR sends keep-alives every 15 seconds and considers the client timed out after 60 seconds according to current server configuration. The client uses automatic reconnect delays of 0, 2, 5, 10, and 30 seconds; after a terminal close it runs its own retry loop for up to 20 attempts.
7. On reconnect, the client rejoins only the current room and refreshes unread counts. It does not record the browser-side close code, selected transport, reconnect attempts, or request failures to a persistent incident log.

The incident-time break point is unknown because no synchronized browser, IIS, server, and SQL timeline exists.

### Confirmed Findings

| Finding | Evidence | Incident conclusion |
| --- | --- | --- |
| Repeated host initialization is present in the available 2026-07-27 logs. | Five `Scheduler Started` entries appear at 11:24:28, 11:39:31, 11:48:01, 11:51:49, and 11:55:51. Quartz is configured in `Program.cs` during application startup. | Confirmed host initialization events; restart cause and production correlation are unknown. |
| Normal SignalR connect/disconnect cycles are logged. | `Logs/log-20260727.txt` records successful negotiate, `SignalR Connected`, clean `SignalR Disconnected`, and completed `HTTP CONNECT` requests. | These records are not a freeze capture and do not prove a SignalR failure. |
| A current archive run completed in the available logs. | On 2026-07-23, `Stored Procedure Started` was logged at 12:05:08.280 and `Stored Procedure Finished` at 12:05:11.752. | This run lasted about 3.5 seconds. No freeze report, SQL blocking evidence, or common incident timestamp is available. |
| The current archive path is asynchronous and time bounded. | `DatabaseJobService` uses `OpenAsync`, `ExecuteNonQueryAsync`, a 300-second command timeout, and a linked 5-minute cancellation token. | This source review does not prove the version deployed during any incident had this behavior. |
| The archive procedure takes transactions around batches. | `Scripts/sp_ArchiveOldChatData.sql` uses an explicit transaction for each archive/delete batch. | This can only be evaluated for blocking with SQL Server snapshots captured during an incident. |
| Current SignalR resiliency settings exist. | `Program.cs` sets `KeepAliveInterval=15s` and `ClientTimeoutInterval=60s`; `Index.cshtml` configures automatic reconnect and terminal-close retry. | Settings alone do not establish that IIS WebSockets, proxy behavior, or authentication succeeded in production. |
| Current cookie settings are not a 60-minute hard expiry. | `Program.cs` sets `ExpireTimeSpan=8 hours` and `SlidingExpiration=true`; hub/API login redirects return 401. | The earlier 60-minute-expiry claim cannot be used as the current root cause without a production deployment/version and incident timestamp. |
| No direct blocking primitives were found in the active application source scan. | No active `.Result`, `.Wait()`, `Thread.Sleep`, `lock`, `SemaphoreSlim`, `Mutex`, or `Timer` use was found in application C# files. The watchdog registration is commented out. | This does not rule out ThreadPool starvation from external/IIS/SQL work; it only rules out those scanned source patterns. |
| The available logs are insufficient for correlation. | They do not attach process ID, host instance ID, build version, selected SignalR transport, browser close details, IIS status/substatus, worker-process recycle reason, SQL session data, CPU, GC, or ThreadPool history to an incident ID. | A primary root cause cannot be validated from the stored logs. |

### Hypotheses Requiring Evidence

| Hypothesis | Evidence found | Evidence missing to validate or reject it |
| --- | --- | --- |
| IIS/ANCM worker-process restart or recycle interrupts all clients. | Repeated host initialization entries exist in repository logs. | IIS access logs, W3SVC-WP/WAS events, application-pool recycle history/config, deployment history, PID/start-time correlation, and an incident timestamp. |
| SignalR connection or reverse-proxy transport fails while the page remains loaded. | The client has reconnect handling; normal connection lifecycle is logged. | Browser WebSocket close code/reason and frames, selected transport, `/negotiate` status, IIS proxy/WebSocket logs/config, and server-side disconnect exception at the same time. |
| SQL blocking during archive or chat writes delays hub and HTTP work. | The archive procedure writes/deletes in transactions; chat sends and mark-read operations write related tables. | SQL blocking-chain, wait, lock, deadlock, query-duration, and session snapshots captured during the freeze, correlated to the archive job and request IDs. |
| Browser main-thread or memory pressure freezes the UI before requests leave the browser. | The page appends chat DOM nodes and registers event handlers, but no browser exception is present in repository logs. | Chrome Performance recording, heap snapshots before/during incident, console export, Network HAR with Preserve log, and proof that a test request does not reach IIS. |
| Authentication/data-protection failure prevents reconnection. | The code returns 401 for hub/API login redirects and logs `AUTH FAILED` if `GetUserId()` has no claim. | Incident-time 401s, `AUTH FAILED` logs, cookie expiry timestamps, production key-directory ACLs, and server-instance key-store evidence. |
| ASP.NET Core process resource exhaustion blocks requests. | A manual `GET /Home/ping` endpoint exposes an instantaneous ThreadPool snapshot. | Time-series CPU, memory, GC, ThreadPool queue/thread counts, socket counts, process dump/trace, and a ping response captured during the freeze. |

### Root Cause Validation Matrix

| Symptom | Explained by a proven cause? | Evidence status |
| --- | --- | --- |
| Entire UI freezes | No | No Chrome performance or main-thread trace at incident time. |
| SignalR stops working | No | No close/error/transport trace synchronized to a freeze. |
| Backend receives no HTTP requests | No | No browser HAR and IIS access-log correlation for the reported time. |
| Refresh temporarily recovers | No | Refresh behavior is reported, but no before/after connection, auth, or server evidence is stored. |
| Production-only behavior | No | Production IIS configuration, environment variables, topology, and build identity are absent. |
| Random/intermittent occurrence | No | No incident table with timestamps, users, duration, and concurrent job/server events exists. |
| No visible browser exception | No | Browser console export has not been collected. |
| No server crash | No | Application, WAS, W3SVC-WP, and Windows crash/recycle evidence is absent. |
| Localhost works | No | Local and production versions/configurations have not been compared. |

### Exact Evidence Required on the Next Occurrence

Use one incident timestamp in UTC and an incident ID for every artifact. Record affected user, browser, URL, app-pool name, server name, process ID, and start/end time before collecting any conclusion.

#### Browser

1. Chrome DevTools: enable Network `Preserve log`, reproduce/observe the freeze, then export HAR including `/hubs/basicchat/negotiate`, WebSocket/CONNECT activity, and a manual `GET /Home/ping` request.
2. Export Console with log level `Verbose`, including SignalR `onreconnecting`, `onreconnected`, and `onclose` data.
3. Capture a 30-60 second Performance recording during the frozen state. This determines whether the JavaScript main thread is blocked.
4. Capture Heap Snapshots before and during a prolonged session, and compare detached nodes/listener counts.
5. In the Network WebSocket inspector, preserve the close code, reason, selected transport, last received frame, and last sent frame.

#### IIS and Windows

Run the following on the production host for the exact incident window, replacing the placeholders. Save output outside the application deployment directory.

```powershell
$incidentStart = [datetime]'2026-07-28T00:00:00Z'
$incidentEnd = $incidentStart.AddMinutes(30)

Get-WinEvent -FilterHashtable @{ LogName = 'System'; StartTime = $incidentStart; EndTime = $incidentEnd } |
  Where-Object { $_.ProviderName -match 'WAS|W3SVC|IIS|Service Control Manager' } |
  Format-List TimeCreated, ProviderName, Id, LevelDisplayName, Message

Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = $incidentStart; EndTime = $incidentEnd } |
  Where-Object { $_.ProviderName -match 'Application Error|.NET Runtime|IIS AspNetCore Module V2|Windows Error Reporting' } |
  Format-List TimeCreated, ProviderName, Id, LevelDisplayName, Message

& $env:windir\system32\inetsrv\appcmd.exe list apppool "<APP_POOL_NAME>" /config /xml
& $env:windir\system32\inetsrv\appcmd.exe list wp
Get-Process dotnet,w3wp -ErrorAction SilentlyContinue | Select-Object Id, ProcessName, StartTime, CPU, WorkingSet64
```

Collect the matching IIS W3C log rows, IIS Failed Request Tracing (FREB) traces, application-pool rapid-fail/recycle settings, site bindings, WebSocket Protocol feature state, reverse-proxy/ARR configuration, and deployment/config-history entries. The repository has no `web.config`, so these cannot be inferred from source.

#### ASP.NET Core Process

Collect counters before and during the next event. Replace `<PID>` with the current production `dotnet` or `w3wp` process ID.

```powershell
dotnet-counters monitor --process-id <PID> System.Runtime Microsoft.AspNetCore.Hosting
dotnet-trace collect --process-id <PID> --duration 00:00:30 --output C:\Incident\chat-freeze.nettrace
dotnet-dump collect --process-id <PID> --output C:\Incident\chat-freeze.dmp
```

Required counters: process CPU, working set, GC heap size, allocation rate, Gen 2 collections, ThreadPool queue length, ThreadPool thread count, exception rate, active requests, and socket/connection counts. A dump is a production-impacting capture and must be approved by the server owner before collection.

#### SQL Server

Run the blocking, long-running request, lock, and connection-pool DMV queries already documented in Section 16 during the freeze, not after recovery. Also capture deadlock Extended Events/system_health output, SQL Server error log entries, `sp_WhoIsActive` output if approved, execution plans/query-store data for long operations, and the `sp_ArchiveOldChatData` start/end times. Record the SQL session ID and correlate it with application request/connection IDs.

#### Application Logging and Metrics Gaps

Before the next occurrence, add production-safe observability rather than a speculative functional fix:

- Log application start, stopping, stopped, process ID, machine name, instance ID, deployment/build SHA, and UTC startup time through `IHostApplicationLifetime`.
- Enrich every request/hub log with process ID, instance ID, trace ID, connection ID, authenticated user ID (where permitted), and selected SignalR transport.
- Persist client-side SignalR close code/reason, reconnect attempt/result, and browser request failures to a central endpoint or telemetry service with an incident ID.
- Poll and retain `/Home/ping` or equivalent process metrics externally. The current endpoint is a point-in-time diagnostic only; `LastActivityTime` is updated by hub activity, not every HTTP request.
- Retain IIS, Windows, SQL, and application logs in one time-synchronized store. Confirm all hosts use NTP and UTC timestamps.

### Correlation Procedure

1. Establish the first failed browser action and UTC timestamp from the HAR/Performance recording.
2. Determine whether that HTTP request reached IIS. If not, investigate browser/network/proxy; if it reached IIS but has no application completion, inspect IIS/ANCM and process trace.
3. Match the SignalR connection ID and request trace ID to server hub/request logs. Identify the precise disconnect, error, or absence of activity.
4. Match the same window to application-process start/stop events, IIS app-pool recycle events, CPU/GC/ThreadPool counters, and socket data.
5. Match the same window to SQL blocking/deadlock/long-running session data and the archive job timeline.
6. Reject every hypothesis that lacks the matching event chain. Declare a root cause only after one chain explains all symptoms without contradictory evidence.

### Fix and Verification Status

There is no evidence-backed "exact fix" for the reported freeze yet. Making IIS, SQL, browser, or application changes as a declared root-cause repair would be speculative.

The immediate corrective action is the observability/deployment checklist above. After a correlated incident identifies a cause, the implementation must include: a scoped change linked to the captured evidence, an IIS/Windows/SQL rollback plan where relevant, staging reproduction using the same deployment configuration, production deployment with build/version logging, and success criteria of no repeated correlated failure across the agreed observation period. Until then, the only defensible final conclusion is: **Root Cause cannot yet be proven from available evidence.**
