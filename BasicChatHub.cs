using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SignalRMVC.Areas.Identity.Data;
using SignalRMVC.CustomClasses;
using SignalRMVC.FreezeMonitor.Services;
using SignalRMVC.Models;
using System.Security.Claims;
using System.Collections.Concurrent;

namespace SignalRMVC
{
    public class BasicChatHub : Hub
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<BasicChatHub> _logger;
        private readonly SignalRHealthService _signalRHealthService;

        // ❌ Removed AppDbContext from constructor
        // Reason: DbContext is NOT thread-safe inside SignalR Hub
        // We'll create DbContext per request using scopeFactory
        public BasicChatHub(
            UserManager<ApplicationUser> userManager,
            IServiceScopeFactory scopeFactory,
            ILogger<BasicChatHub> logger,
            SignalRHealthService signalRHealthService)
        {
            _userManager = userManager;
            _scopeFactory = scopeFactory;
            _logger = logger;
            _signalRHealthService = signalRHealthService;
        }

        // =====================================================
        // Connection Events
        // =====================================================
        public override async Task OnConnectedAsync()
        {
            try
            {
                AppHealthTracker.UpdateActivity();
                AppHealthTracker.TrackConnect();

                var userId = GetUserId();
                _signalRHealthService.TrackConnected(Context.ConnectionId, userId);
                _logger.LogInformation(
                    "SignalR Connected | UserId={UserId} | ConnId={ConnId} | ActiveConns={Active}",
                    userId, Context.ConnectionId, AppHealthTracker.ActiveConnections);

                await JoinRoleGroupIfAny(userId);
                await JoinAllUserMappedGroups(userId);
                await base.OnConnectedAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OnConnectedAsync failed | ConnId={ConnId}", Context.ConnectionId);
                await base.OnConnectedAsync();
            }
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            try
            {
                AppHealthTracker.UpdateActivity();
                AppHealthTracker.TrackDisconnect();
                _signalRHealthService.TrackDisconnected(Context.ConnectionId);

                _joinedGroupsByConnection.TryRemove(Context.ConnectionId, out _);

                if (exception != null)
                    _logger.LogWarning(exception,
                        "SignalR Disconnected (error) | ConnId={ConnId} | ActiveConns={Active}",
                        Context.ConnectionId, AppHealthTracker.ActiveConnections);
                else
                    _logger.LogInformation(
                        "SignalR Disconnected (clean) | ConnId={ConnId} | ActiveConns={Active}",
                        Context.ConnectionId, AppHealthTracker.ActiveConnections);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OnDisconnectedAsync cleanup failed | ConnId={ConnId}", Context.ConnectionId);
            }
            finally
            {
                await base.OnDisconnectedAsync(exception);
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
            {
                _logger.LogWarning(
                    "AUTH FAILED | ConnId={ConnId} | IsAuthenticated={IsAuth} | Transport={Transport}",
                    Context.ConnectionId,
                    httpContext?.User?.Identity?.IsAuthenticated,
                    httpContext?.WebSockets?.IsWebSocketRequest);
                throw new HubException("User is not authenticated.");
            }

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

        // Track groups joined per connection (bounded + cleaned up on disconnect)
        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _joinedGroupsByConnection = new();

        [Authorize]
        public async Task JoinGroup(string sender)
        {
            var userId = GetUserId();
            await JoinRoleGroupIfAny(userId);
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
        // Reactions (group messages only, one reaction per user per message)
        // =====================================================
        // Single source of truth: the first 6 are the quick-pick row, the rest appear under the "+" button.
        public static readonly string[] ReactionEmojis =
        {
            "✅", "❌", "🔥","❤️", "💯", "🙏",
            "👎", "😀", "😃", "😄", "😁", "😆", "😅", "🤣", "😊", "😇",
            "🙂", "😉", "😍", "😘", "😋", "😎", "🤔", "😐", "😴", "😭",
            "😡", "😱", "😬", "🤗", "🥳", "🤝", "👏", "🙌", "💪", "👌",
            "✌️",  "😮", "🎉", "👍", "😂" , "😢", "⭐", "👀", "🤦"
        };

        private static readonly HashSet<string> AllowedReactions = new(ReactionEmojis, StringComparer.Ordinal);

        public async Task ToggleReaction(int messageId, string emoji)
        {
            var userId = GetUserId();

            try
            {
                AppHealthTracker.UpdateActivity();

                if (string.IsNullOrEmpty(emoji) || !AllowedReactions.Contains(emoji))
                {
                    await Clients.Caller.SendAsync("Error", "Reaction not allowed.");
                    return;
                }

                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var message = await context.ChatMessages
                    .AsNoTracking()
                    .Where(m => m.Id == messageId)
                    .Select(m => new { m.GroupName, m.IsDelete })
                    .FirstOrDefaultAsync();

                if (message == null || message.IsDelete || string.IsNullOrEmpty(message.GroupName))
                {
                    await Clients.Caller.SendAsync("Error", "Message not found.");
                    return;
                }

                var isActiveMember = await (
                    from room in context.ChatRoom
                    join mapping in context.GroupUserMapping on room.Id equals mapping.GroupId
                    where room.Name == message.GroupName && !room.isDelete
                          && mapping.UserId == userId && mapping.Active
                    select mapping.Id
                ).AnyAsync();

                if (!isActiveMember)
                {
                    await Clients.Caller.SendAsync("Error", "You are not a member of this group.");
                    return;
                }

                var existing = await context.ChatMessageReactions
                    .FirstOrDefaultAsync(r => r.ChatMessageId == messageId && r.UserId == userId);

                string? resultEmoji = emoji;

                if (existing == null)
                {
                    context.ChatMessageReactions.Add(new ChatMessageReaction
                    {
                        ChatMessageId = messageId,
                        UserId = userId,
                        Emoji = emoji,
                        CreatedOn = DateTime.Now
                    });
                }
                else if (existing.Emoji == emoji)
                {
                    context.ChatMessageReactions.Remove(existing); // same emoji again = remove
                    resultEmoji = null;
                }
                else
                {
                    existing.Emoji = emoji; // different emoji replaces the previous one
                    existing.CreatedOn = DateTime.Now;
                }

                try
                {
                    await context.SaveChangesAsync();
                }
                catch (DbUpdateException ex)
                {
                    // Concurrent double-click hit the unique index; the other request already won.
                    _logger.LogInformation(ex, "ToggleReaction conflict | MessageId={MessageId} | UserId={UserId}", messageId, userId);
                    return;
                }

                await Clients.Group(message.GroupName).SendAsync(
                    "ReactionChanged", messageId, Context.User?.Identity?.Name ?? string.Empty, resultEmoji);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ToggleReaction failed | MessageId={MessageId}", messageId);
                await LogAsync(userId, "ToggleReaction", ex.Message + " InnerException " + (ex.InnerException?.Message ?? ""));
                await Clients.Caller.SendAsync("Error", "Could not save reaction.");
            }
        }

        // =====================================================
        // Send Message to Room
        // =====================================================
        public async Task SendMessageToRoom(string roomName, string user, string message, string? clientMessageId = null, int replyToMessageId = 0)
        {
            var userId = GetUserId();

            try
            {
                AppHealthTracker.UpdateActivity();

                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                if (!string.IsNullOrWhiteSpace(clientMessageId))
                {
                    // Idempotency guard (prevents duplicates on reconnect/retry)
                    var exists = await context.ChatMessages
                        .AsNoTracking()
                        .AnyAsync(m => m.SenderId == userId && m.ClientMessageId == clientMessageId);
                    if (exists)
                    {
                        _logger.LogInformation("Duplicate room message suppressed | SenderId={SenderId} | ClientMessageId={ClientMessageId}", userId, clientMessageId);
                        return;
                    }
                }

                string replyToMessageSender = string.Empty;
                string? replyToMessageText = null;
                bool replyToMessageDeleted = false;

                if (replyToMessageId > 0)
                {
                    var parentReplyMessage = await context.ChatMessages
                        .AsNoTracking()
                        .FirstOrDefaultAsync(m => m.Id == replyToMessageId && m.GroupName == roomName);

                    if (parentReplyMessage == null)
                    {
                        await Clients.Caller.SendAsync("Error", "Reply target not found.");
                        return;
                    }

                    var replySender = await _userManager.FindByIdAsync(parentReplyMessage.SenderId ?? string.Empty);
                    replyToMessageSender = replySender?.UserName ?? replySender?.FullName ?? string.Empty;
                    replyToMessageText = parentReplyMessage.IsDelete ? "Message deleted" : parentReplyMessage.Message;
                    replyToMessageDeleted = parentReplyMessage.IsDelete;
                }

                var chatMessage = new ChatMessage
                {
                    SenderId = userId,
                    ReceiverId = null,
                    Message = message,
                    GroupName = roomName,
                    ReplyToMessageId = replyToMessageId,
                    CreatedOn = DateTime.Now,
                    ClientMessageId = clientMessageId
                };

                // Create unread status entries for other users in group
                var room = await context.ChatRoom.FirstOrDefaultAsync(r => r.Name == roomName);
                var recipientIds = new List<string>();

                if (room != null)
                {
                    recipientIds = await context.GroupUserMapping
                        .AsNoTracking()
                        .Where(g => g.GroupId == room.Id && g.UserId != userId && g.Active)
                        .Select(g => g.UserId)
                        .ToListAsync();

                    var readStatuses = recipientIds.Select(recipientId => new ChatMessageReadStatus
                    {
                        ChatMessage = chatMessage, // ✅ lets us SaveChanges once (EF will insert message then statuses)
                        UserId = recipientId,
                        IsRead = false,
                        CreatedOn = DateTime.Now
                    }).ToList();

                    context.ChatMessageReadStatuses.AddRange(readStatuses);
                }

                context.ChatMessages.Add(chatMessage);
                await context.SaveChangesAsync();

                var messageId = chatMessage.Id;
                var messageTime = chatMessage.CreatedOn.HasValue
                    ? chatMessage.CreatedOn.Value.ToString("dd-MM-yy HH:mm")
                    : "";

                object? replyMessageDto = replyToMessageId > 0
                    ? new
                    {
                        id = replyToMessageId,
                        message = replyToMessageText,
                        senderName = replyToMessageSender
                    }
                    : null;

                var messageDto = new
                {
                    id = messageId,
                    senderId = userId,
                    senderName = user,
                    message,
                    messageTime,
                    receiver = roomName,
                    isGroup = true,
                    replyToMessageId,
                    replyToMessageSender,
                    replyToMessageText,
                    replyToMessageDeleted,
                    replyMessage = replyMessageDto
                };

                // ✅ Broadcast MessageReceived ONLY to the SignalR room group (prevents double delivery bug)
                await Clients.Group(roomName).SendAsync("MessageReceived", messageDto);

                // ✅ Push unread deltas to all affected recipients
                if (room != null)
                {
                    foreach (var recipientId in recipientIds)
                    {
                        await Clients.User(recipientId)
                            .SendAsync("ReceiveUnreadDelta", room.Id.ToString(), roomName, 1, true);
                    }
                }
            }
            catch (Exception ex)
            {
                await LogAsync(userId, "SendMessageToRoom", ex.Message + " InnerException " + (ex.InnerException?.Message ?? ""));
            }
        }

        // =====================================================
        // Send Message to User
        // =====================================================
        public async Task SendMessageToUser(string user, string receiver, string message, string? clientMessageId = null)
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

                if (!string.IsNullOrWhiteSpace(clientMessageId))
                {
                    // Idempotency guard (prevents duplicates on reconnect/retry)
                    var exists = await context.UsersMessage
                        .AsNoTracking()
                        .AnyAsync(m => m.SenderId == userId && m.ClientMessageId == clientMessageId);
                    if (exists)
                    {
                        _logger.LogInformation("Duplicate user message suppressed | SenderId={SenderId} | ClientMessageId={ClientMessageId}", userId, clientMessageId);
                        return;
                    }
                }

                var chatMessage = new UsersMessage
                {
                    SenderId = userId,
                    ReceiverId = receiver,
                    Message = message,
                    CreatedOn = DateTime.Now,
                    ClientMessageId = clientMessageId
                };

                var readStatuses = new UsersMessageReadStatus
                {
                    ChatMessage = chatMessage, // ✅ lets us SaveChanges once (EF will insert message then status)
                    SenderId = userId,
                    ReceiverId = receiver,
                    IsRead = false,
                    CreatedOn = DateTime.Now
                };

                context.UsersMessageReadStatus.Add(readStatuses);
                await context.SaveChangesAsync();

                var messageId = chatMessage.Id;
                var messageTime = chatMessage.CreatedOn.HasValue
                    ? chatMessage.CreatedOn.Value.ToString("dd-MM-yy HH:mm")
                    : "";

                var messageDto = new
                {
                    id = messageId,
                    senderId = userId,
                    senderName = user,
                    message,
                    messageTime,
                    receiver,
                    isGroup = false,
                    replyToMessageId = 0,
                    replyToMessageSender = string.Empty,
                    replyToMessageText = (string?)null,
                    replyToMessageDeleted = false,
                    replyMessage = (object?)null
                };

                // ✅ Send standardized DTO object to receiver and sender
                await Clients.User(receiver).SendAsync("MessageReceived", messageDto);
                await Clients.User(userId).SendAsync("SendMessageUser", messageDto);

                // ✅ Avoid DB group-by storms: push delta only to affected receiver
                await Clients.User(receiver).SendAsync("ReceiveUnreadDelta", senderId, user, 1, false);
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
            try
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

                // ✅ EF Core 8 bulk delete (no load into memory)
                await context.ChatMessageReadStatuses
                    .Where(s => s.UserId == userId && s.ChatMessage.GroupName == roomName)
                    .ExecuteDeleteAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MarkMessagesAsRead failed | RoomId={RoomId}", roomId);
            }
        }

        // =====================================================
        // Mark Personal Messages as Read
        // =====================================================
        public async Task P_To_P_MarkMessagesAsRead(string UserId)
        {
            try
            {
                var receiver = GetUserId();

                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                // ✅ EF Core 8 bulk delete (no load into memory)
                await context.UsersMessageReadStatus
                    .Where(s => s.SenderId == UserId && s.ReceiverId == receiver)
                    .ExecuteDeleteAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "P_To_P_MarkMessagesAsRead failed | UserId={UserId}", UserId);
            }
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

        private async Task JoinRoleGroupIfAny(string userId)
        {
            var role = (await GetUserRoles(userId)).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(role))
                return;

            var groups = _joinedGroupsByConnection.GetOrAdd(Context.ConnectionId, _ => new ConcurrentDictionary<string, byte>());
            if (groups.TryAdd(role, 0))
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, role);
            }
        }

        private async Task JoinAllUserMappedGroups(string userId)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                var userGroupNames = await (
                    from mapping in context.GroupUserMapping
                    join room in context.ChatRoom on mapping.GroupId equals room.Id
                    where mapping.UserId == userId && mapping.Active
                    select room.Name
                ).ToListAsync();

                foreach (var groupName in userGroupNames)
                {
                    if (!string.IsNullOrWhiteSpace(groupName))
                    {
                        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to join user mapped groups for UserId={UserId}", userId);
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
