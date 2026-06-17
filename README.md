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

