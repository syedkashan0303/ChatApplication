using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SignalRMVC.Areas.Identity.Data;
using SignalRMVC.CustomClasses;
using SignalRMVC.Models;
using System.Security.Claims;

namespace SignalRMVC
{
    public class BasicChatHub : Hub
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<BasicChatHub> _logger;

        // ❌ Removed AppDbContext from constructor
        // Reason: DbContext is NOT thread-safe inside SignalR Hub
        // We'll create DbContext per request using scopeFactory
        public BasicChatHub(
            UserManager<ApplicationUser> userManager,
            IServiceScopeFactory scopeFactory,
            ILogger<BasicChatHub> logger)
        {
            _userManager = userManager;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        // =====================================================
        // Connection Events
        // =====================================================
        public override async Task OnConnectedAsync()
        {
            try
            {
                AppHealthTracker.UpdateActivity();

                var userId = GetUserId();
                var roles = await GetUserRoles(userId);

                await base.OnConnectedAsync();
            }
            catch (Exception ex)
            {
                // ❌ Before: empty catch (silently ignored)
                // ✅ Now: log it (server pe debugging easy)
                _logger.LogError(ex, "❌ OnConnectedAsync failed");
                await base.OnConnectedAsync();
            }
        }

        // =====================================================
        // Force logout
        // =====================================================
        public async Task ForceLogout()
        {
            await Clients.All.SendAsync("RedirectToLogin");
        }

        // =====================================================
        // Get UserId
        // =====================================================
        public string GetUserId()
        {
            var httpContext = Context.GetHttpContext();
            var userId = httpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(userId))
                throw new HubException("User is not authenticated.");

            return userId;
        }

        // =====================================================
        // Get Roles
        // =====================================================
        public async Task<IList<string>> GetUserRoles(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return new List<string>();

            var roles = await _userManager.GetRolesAsync(user);
            return roles;
        }

        // =====================================================
        // Edit Message
        // =====================================================
        public async Task EditMessage(int messageId, string newContent, string roomName, string chatUserId = "")
        {
            var userId = GetUserId();

            try
            {
                AppHealthTracker.UpdateActivity();

                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                if (!string.IsNullOrEmpty(chatUserId))
                {
                    var usersMessage = await context.UsersMessage.FindAsync(messageId);

                    if (usersMessage == null)
                    {
                        await LogAsync(userId, "EditMessage", "message is null");
                        await Clients.Caller.SendAsync("Error", "Message not found.");
                        return;
                    }

                    if (usersMessage.SenderId != userId || usersMessage.ReceiverId != chatUserId)
                    {
                        await LogAsync(userId, "EditMessage", "(SenderId is not match with login customer)");
                        await Clients.Caller.SendAsync("Error", "Unauthorized to edit this message.");
                        return;
                    }

                    var messageLogs = new EditedMessagesLog
                    {
                        MessageId = messageId,
                        Message = usersMessage.Message,
                        EditedBy = userId,
                        EditedOn = DateTime.Now,
                        GroupName = userId + " ==> " + chatUserId
                    };

                    context.EditedtMessagesLogs.Add(messageLogs);
                    await context.SaveChangesAsync();

                    usersMessage.Message = newContent;
                    await context.SaveChangesAsync();

                    await Clients.User(chatUserId).SendAsync("MessageEdited", messageId, newContent);
                }
                else
                {
                    var message = await context.ChatMessages.FindAsync(messageId);

                    if (message == null)
                    {
                        await LogAsync(userId, "EditMessage", "message is null");
                        await Clients.Caller.SendAsync("Error", "Message not found.");
                        return;
                    }

                    if (message.SenderId != userId)
                    {
                        await LogAsync(userId, "EditMessage", "(SenderId is not match with login customer)");
                        await Clients.Caller.SendAsync("Error", "Unauthorized to edit this message.");
                        return;
                    }

                    var msgLogs = new EditedMessagesLog
                    {
                        MessageId = messageId,
                        Message = message.Message,
                        EditedBy = userId,
                        EditedOn = DateTime.Now,
                        GroupName = roomName
                    };

                    context.EditedtMessagesLogs.Add(msgLogs);
                    await context.SaveChangesAsync();

                    message.Message = newContent;
                    await context.SaveChangesAsync();

                    await Clients.Group(roomName).SendAsync("MessageEdited", messageId, newContent);
                }
            }
            catch (Exception ex)
            {
                await LogAsync(userId, "EditMessage", ex.Message + " InnerException " + (ex.InnerException?.Message ?? ""));
                await Clients.Caller.SendAsync("Error", $"Server error: {ex.Message}");
            }
        }

        // =====================================================
        // Delete Message
        // =====================================================
        public async Task DeleteMessage(int messageId, string roomName, string chatUserId = "")
        {
            var userId = "";

            try
            {
                AppHealthTracker.UpdateActivity();

                userId = GetUserId();

                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                if (!string.IsNullOrEmpty(chatUserId))
                {
                    var message = await context.UsersMessage.FindAsync(messageId);

                    if (message == null)
                    {
                        await Clients.Caller.SendAsync("Error", "Message not found in database.");
                        return;
                    }

                    if (message.SenderId != userId)
                    {
                        await Clients.Caller.SendAsync("Error", "You are not allowed to delete this message.");
                        return;
                    }

                    message.IsDelete = true;
                    await context.SaveChangesAsync();

                    await Clients.Group(roomName).SendAsync("MessageDeleted", messageId);
                    await Clients.User(chatUserId).SendAsync("UserMessageDeleted", messageId);
                }
                else
                {
                    var message = await context.ChatMessages.FindAsync(messageId);

                    if (message == null)
                    {
                        await Clients.Caller.SendAsync("Error", "Message not found in database.");
                        return;
                    }

                    if (message.SenderId != userId)
                    {
                        await Clients.Caller.SendAsync("Error", "You are not allowed to delete this message.");
                        return;
                    }

                    message.IsDelete = true;
                    await context.SaveChangesAsync();

                    await Clients.Group(roomName).SendAsync("MessageDeleted", messageId);
                }
            }
            catch (Exception ex)
            {
                await LogAsync(userId, "DeleteMessage", ex.ToString());
                await Clients.Caller.SendAsync("Error", $"Server error: {ex.Message}");
                throw;
            }
        }

        // =====================================================
        // GROUP / ROOM WORKING
        // =====================================================

        // ⚠️ NOTE: static list is shared between all users + all connections
        // I kept it because it was in your code (not removed)
        public static List<string> GroupsJoined { get; set; } = new List<string>();

        [Authorize]
        public async Task JoinGroup(string sender)
        {
            var user = GetUserId();
            var role = (await GetUserRoles(user)).FirstOrDefault();

            if (!string.IsNullOrEmpty(role))
            {
                if (!GroupsJoined.Contains(Context.ConnectionId + ":" + role))
                {
                    GroupsJoined.Add(Context.ConnectionId + ":" + role);
                    await Groups.AddToGroupAsync(Context.ConnectionId, role);
                }
            }
        }

        public async Task JoinRoom(string roomName)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
        }

        public async Task LeaveRoom(string roomName)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomName);
        }

        // =====================================================
        // Send Message to Room
        // =====================================================
        public async Task SendMessageToRoom(string roomName, string user, string message)
        {
            var userId = GetUserId();

            try
            {
                AppHealthTracker.UpdateActivity();

                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var chatMessage = new ChatMessage
                {
                    SenderId = userId,
                    ReceiverId = null,
                    Message = message,
                    GroupName = roomName,
                    CreatedOn = DateTime.Now
                };

                context.ChatMessages.Add(chatMessage);
                await context.SaveChangesAsync();

                var messageId = chatMessage.Id;
                var messageTime = chatMessage.CreatedOn.HasValue
                    ? chatMessage.CreatedOn.Value.ToString("dd-MM-yy HH:mm")
                    : "";

                // Create unread status entries for other users in group
                var room = await context.ChatRoom.FirstOrDefaultAsync(r => r.Name == roomName);
                if (room != null)
                {
                    var groupUsers = await context.GroupUserMapping
                        .Where(g => g.GroupId == room.Id && g.UserId != userId)
                        .Select(g => g.UserId)
                        .ToListAsync();

                    var readStatuses = groupUsers.Select(recipientId => new ChatMessageReadStatus
                    {
                        ChatMessageId = chatMessage.Id,
                        UserId = recipientId,
                        IsRead = false,
                        CreatedOn = DateTime.UtcNow
                    }).ToList();

                    context.ChatMessageReadStatuses.AddRange(readStatuses);
                    await context.SaveChangesAsync();
                }

                //await Clients.Group(roomName).SendAsync(
                //    "MessageReceived",
                //    messageId,
                //    user,
                //    message,
                //    messageTime,
                //    senderId: userId,
                //    receiver: "",
                //    isGroup: true
                //);
                await Clients.Group(roomName).SendAsync(
                    "MessageReceived",
                    messageId,
                    user,
                    message,
                    messageTime,
                    userId,   // senderId
                    "",       // receiver
                    true      // isGroup
                );


                await BroadcastUnreadCount(roomName: roomName);
            }
            catch (Exception ex)
            {
                await LogAsync(userId, "SendMessageToRoom", ex.Message + " InnerException " + (ex.InnerException?.Message ?? ""));
            }
        }

        // =====================================================
        // Send Message to User
        // =====================================================
        public async Task SendMessageToUser(string user, string receiver, string message)
        {
            var userId = GetUserId();
            string senderId = userId;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var receiverUser = await _userManager.FindByIdAsync(receiver);

                if (receiverUser == null)
                    return;

                var chatMessage = new UsersMessage
                {
                    SenderId = userId,
                    ReceiverId = receiver,
                    Message = message,
                    CreatedOn = DateTime.Now
                };

                context.UsersMessage.Add(chatMessage);
                await context.SaveChangesAsync();

                var messageId = chatMessage.Id;
                var messageTime = chatMessage.CreatedOn.HasValue
                    ? chatMessage.CreatedOn.Value.ToString("dd-MM-yy HH:mm")
                    : "";

                var readStatuses = new UsersMessageReadStatus
                {
                    ChatMessageId = chatMessage.Id,
                    SenderId = userId,
                    ReceiverId = receiver,
                    IsRead = false,
                    CreatedOn = DateTime.UtcNow
                };

                context.UsersMessageReadStatus.Add(readStatuses);
                await context.SaveChangesAsync();

                await Clients.User(receiver).SendAsync("MessageReceived", messageId, user, message, messageTime, senderId, receiver, false);
                await Clients.User(userId).SendAsync("SendMessageUser", messageId, user, message, messageTime, senderId, receiver, false);

                await BroadcastUnreadCount(SenderId: userId);
            }
            catch (Exception ex)
            {
                await LogAsync(userId, "SendMessageToUser", ex.Message + " InnerException " + (ex.InnerException?.Message ?? ""));
            }
        }

        // =====================================================
        // Mark Room Messages as Read
        // =====================================================
        public async Task MarkMessagesAsRead(int roomId)
        {
            AppHealthTracker.UpdateActivity();

            var userId = GetUserId();

            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var roomName = await context.ChatRoom
                .Where(r => r.Id == roomId)
                .Select(r => r.Name)
                .FirstOrDefaultAsync();

            if (roomName == null)
                return;

            var unreadMessages = await context.ChatMessageReadStatuses
                .Where(s => s.UserId == userId && s.ChatMessage.GroupName == roomName)
                .ToListAsync();

            foreach (var status in unreadMessages)
            {
                context.Remove(status);
            }

            await context.SaveChangesAsync();
        }

        // =====================================================
        // Mark Personal Messages as Read
        // =====================================================
        public async Task P_To_P_MarkMessagesAsRead(string UserId)
        {
            var receiver = GetUserId();

            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var roomUser = await context.Users
                .Where(r => r.Id == UserId)
                .FirstOrDefaultAsync();

            if (roomUser == null)
                return;

            var unreadMessages = await context.UsersMessageReadStatus
                .Where(s => s.SenderId == UserId && s.ReceiverId == receiver)
                .ToListAsync();

            foreach (var status in unreadMessages)
            {
                context.Remove(status);
            }

            await context.SaveChangesAsync();
        }

        // =====================================================
        // Get Unread Message Counts (FIXED RETURN TYPE)
        // =====================================================
        public async Task<List<object>> GetUnreadMessageCounts()
        {
            var userId = GetUserId();

            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var unreadGroupMessages = await (
                from status in context.ChatMessageReadStatuses
                join chatMsg in context.ChatMessages on status.ChatMessageId equals chatMsg.Id
                join chatRoom in context.ChatRoom on chatMsg.GroupName equals chatRoom.Name
                where status.UserId == userId && !status.IsRead
                group status by new { chatRoom.Id, chatRoom.Name } into g
                select new
                {
                    RoomId = g.Key.Id.ToString(),
                    RoomName = g.Key.Name,
                    Count = g.Count(),
                    isRoom = true
                }
            ).ToListAsync();

            var unreadPersonalMessages = await (
                from status in context.UsersMessageReadStatus
                join chatMsg in context.UsersMessage on status.ChatMessageId equals chatMsg.Id
                join sender in context.Users on chatMsg.SenderId equals sender.Id
                where status.ReceiverId == userId && !status.IsRead
                group status by new { sender.Id, sender.UserName } into g
                select new
                {
                    RoomId = g.Key.Id.ToString(),
                    RoomName = g.Key.UserName,
                    Count = g.Count(),
                    isRoom = false
                }
            ).ToListAsync();

            var result = new List<object>();
            result.AddRange(unreadGroupMessages);
            result.AddRange(unreadPersonalMessages);

            return result;
        }

        // =====================================================
        // Broadcast Unread Count (FIXED ROOMNAME BUG)
        // =====================================================
        public async Task BroadcastUnreadCount(string roomName = "", string SenderId = "")
        {
            using var scope = _scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var unreadList = new List<(string userId, string roomId, string roomName, int count, bool isroom)>();

            if (!string.IsNullOrEmpty(roomName))
            {
                var room = await context.ChatRoom.FirstOrDefaultAsync(r => r.Name == roomName);
                if (room == null) return;

                var unreadCounts = await (
                    from status in context.ChatMessageReadStatuses
                    join chatMsg in context.ChatMessages on status.ChatMessageId equals chatMsg.Id
                    where !status.IsRead && chatMsg.GroupName == roomName
                    group status by status.UserId into g
                    select new
                    {
                        UserId = g.Key,
                        RoomId = room.Id.ToString(),
                        RoomName = roomName,
                        Count = g.Count(),
                        isRoom = true
                    }
                ).ToListAsync();

                unreadList.AddRange(unreadCounts.Select(x =>
                    (userId: x.UserId, roomId: x.RoomId, roomName: x.RoomName, count: x.Count, isroom: x.isRoom)));
            }

            if (!string.IsNullOrEmpty(SenderId))
            {
                var unreadPersonalMessages = await (
                    from status in context.UsersMessageReadStatus
                    join chatMsg in context.UsersMessage on status.ChatMessageId equals chatMsg.Id
                    join sender in context.Users on chatMsg.SenderId equals sender.Id
                    where status.SenderId == SenderId && !status.IsRead
                    group status by new { sender.Id, sender.UserName, chatMsg.ReceiverId } into g
                    select new
                    {
                        UserId = g.Key.ReceiverId,
                        RoomId = g.Key.Id.ToString(),
                        RoomName = g.Key.UserName,
                        Count = g.Count(),
                        isRoom = false
                    }
                ).ToListAsync();

                unreadList.AddRange(unreadPersonalMessages.Select(x =>
                    (userId: x.UserId, roomId: x.RoomId, roomName: x.RoomName, count: x.Count, isroom: x.isRoom)));
            }

            foreach (var userUnread in unreadList)
            {
                // ✅ FIX: roomName was wrong before
                await Clients.User(userUnread.userId)
                    .SendAsync("ReceiveUnreadCount", userUnread.roomId, userUnread.roomName, userUnread.count, userUnread.isroom);
            }
        }

        // =====================================================
        // Log Async (FIXED: Use Scoped Context)
        // =====================================================
        public async Task LogAsync(string userId, string actionName, string details = null)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var log = new ChatLog
                {
                    UserId = userId,
                    ActionName = actionName,
                    Details = details
                };

                context.ChatLogs.Add(log);
                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ LogAsync failed | Action={Action}", actionName);
            }
        }
    }
}
