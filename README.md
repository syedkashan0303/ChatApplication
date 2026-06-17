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

## 15. Group Message Reply Thread Feature

Added: 2026-06-17

This feature allows multiple users to reply to a single group message in a threaded view without cluttering the main chat window. Reply threads are available on group chat messages only. Private (one-to-one) chat does not support replies.

### How it works

- Every group message displays a reply icon below the bubble.
- If replies exist the icon shows a count badge: `↩ Reply 5`.
- Clicking the icon opens a Bootstrap modal showing the original message preview, the full reply thread, and a reply input.
- Replies are loaded on demand via HTTP GET when the modal opens — they are not preloaded for all messages.
- Sending a reply invokes the SignalR hub method `SendReply`. The hub saves the reply and broadcasts a `ReplyReceived` event to every connected user in that group.
- All connected users receive the updated reply count in real time. If any user has the modal open for that same message, the new reply appears live without a refresh.

### New database table

Table: `MessageReplies`

| Column | Type | Notes |
|---|---|---|
| `Id` | `bigint IDENTITY` | Primary key |
| `ParentMessageId` | `int NOT NULL` | FK → `ChatMessages.Id` (CASCADE DELETE) |
| `ReplyText` | `nvarchar(1000)` | Max 1000 characters |
| `UserId` | `nvarchar(450)` | FK → `AspNetUsers.Id` (NO ACTION) |
| `CreatedOn` | `datetime2` | Set on insert |
| `UpdatedOn` | `datetime2` | Nullable |
| `IsDeleted` | `bit` | Soft delete flag |

Migration file: [Migrations/20260617000000_AddMessageReplies.cs](Migrations/20260617000000_AddMessageReplies.cs)

Apply with:
```bash
dotnet ef database update
```

### New and modified files

| File | Change |
|---|---|
| [Models/MessageReply.cs](Models/MessageReply.cs) | New entity |
| [Areas/Identity/Data/AppDbContext.cs](Areas/Identity/Data/AppDbContext.cs) | Added `DbSet<MessageReply> MessageReplies`; FK configured with `NoAction` on user delete |
| [Migrations/AppDbContextModelSnapshot.cs](Migrations/AppDbContextModelSnapshot.cs) | Updated snapshot to include `MessageReply` |
| [BasicChatHub.cs](BasicChatHub.cs) | Added `SendReply(int parentMessageId, string replyText)` hub method |
| [Controllers/HomeController.cs](Controllers/HomeController.cs) | Added `GET /Home/GetReplies`; updated `GetMessagesByRoom` to include `replyCount` per message |
| [Views/Home/Index.cshtml](Views/Home/Index.cshtml) | Added reply modal, reply button in message renderer, `ReplyReceived` SignalR event, reply JS functions |

### New SignalR hub method

`SendReply(int parentMessageId, string replyText)`
- Validates text is non-empty and ≤ 1000 characters.
- Verifies the parent message exists and is not deleted.
- Saves the reply to `MessageReplies`.
- Broadcasts `ReplyReceived(replyDto, replyCount)` to `Clients.Group(groupName)`.

### New client SignalR event

`ReplyReceived(reply, replyCount)`
- Updates the reply count badge on the parent message bubble.
- If the reply modal is open for that message, appends the new reply live.

### New HTTP endpoint

`GET /Home/GetReplies?parentMessageId={id}&page={n}&pageSize={n}`
- Requires authentication.
- Returns replies sorted oldest-first.
- Page size is capped at 50.
- Used by the modal on open to load existing replies.

### Security notes

- Reply text is rendered with `.text()` in jQuery — XSS is not possible.
- Maximum reply length is enforced on both client (character counter) and server (validation in hub).
- Only authenticated users can invoke `SendReply`.

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

