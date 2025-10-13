using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.DotNet.Scaffolding.Shared.Messaging;
using Microsoft.EntityFrameworkCore;
using NuGet.Protocol.Plugins;
using SignalRMVC.Areas.Identity.Data;
using SignalRMVC.CustomClasses;
using SignalRMVC.Models;
using System.Security.Claims;
using static SignalRMVC.Controllers.HomeController;

namespace SignalRMVC
{
    public class BasicChatHub : Hub
    {
        private readonly AppDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IServiceScopeFactory _scopeFactory;

        public BasicChatHub(UserManager<ApplicationUser> userManager, AppDbContext context, IServiceScopeFactory scopeFactory)
        {
            _userManager = userManager;
            _context = context;
            _scopeFactory = scopeFactory;
        }
        // Called when a client connects to the hub. Tracks activity and retrieves user roles.
        public override async Task OnConnectedAsync()
        {
            try
            {
                AppHealthTracker.UpdateActivity();

                var httpContext = Context.GetHttpContext();
                var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var roles = await GetUserRoles(userId);
                // Now you can use the userId as needed
                await base.OnConnectedAsync();
            }
            catch (Exception es)
            {

            }
        }

        // Forces all connected clients to logout by sending a redirect message.
        public async Task ForceLogout()
        {
            await Clients.All.SendAsync("RedirectToLogin");
        }

        // Retrieves the current user's ID from the connection context.
        public string GetUserId()
        {
            var httpContext = Context.GetHttpContext();
            var userId = httpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (userId == null)
                throw new HubException("User is not authenticated.");
            return userId;
        }

        // Gets the roles assigned to a user by their user ID.
        public async Task<IList<string>> GetUserRoles(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            var roles = await _userManager.GetRolesAsync(user);
            return roles;
        }

        // Allows a user to edit their own message in a chat room and logs the edit.
        public async Task EditMessage(int messageId, string newContent, string roomName , string chatUserId = "")
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
                        await LogAsync(userId, "EditMessage", " message is null");

                        await Clients.Caller.SendAsync("Error", "Message not found.");
                        return;
                    }

                    if (usersMessage.SenderId != userId || usersMessage.ReceiverId != chatUserId)
                    {
                        await LogAsync(userId, "EditMessage", " (SenderId is not match with login customer)");
                        await Clients.Caller.SendAsync("Error", "Unauthorized to edit this message.");
                        return;
                    }
                    var messageLogs = new EditedMessagesLog();
                    messageLogs.MessageId = messageId;
                    messageLogs.Message = usersMessage.Message;
                    messageLogs.EditedBy = userId;
                    messageLogs.EditedOn = DateTime.Now;
                    messageLogs.GroupName = userId + " ==> "+ chatUserId;
                    context.EditedtMessagesLogs.Add(messageLogs);
                    context.SaveChanges();

                    usersMessage.Message = newContent;
                    await context.SaveChangesAsync();

                    await Clients.User(chatUserId).SendAsync("MessageEdited", messageId, newContent);

                }
                else
                {
                    var message = await context.ChatMessages.FindAsync(messageId);
                    if (message == null)
                    {
                        await LogAsync(userId, "EditMessage", " message is null");

                        await Clients.Caller.SendAsync("Error", "Message not found.");
                        return;
                    }

                    if (message.SenderId != userId)
                    {
                        await LogAsync(userId, "EditMessage", " (SenderId is not match with login customer)");
                        await Clients.Caller.SendAsync("Error", "Unauthorized to edit this message.");
                        return;
                    }
                    var msgLogs = new EditedMessagesLog();
                    msgLogs.MessageId = messageId;
                    msgLogs.Message = message.Message;
                    msgLogs.EditedBy = userId;
                    msgLogs.EditedOn = DateTime.Now;
                    msgLogs.GroupName = roomName;
                    context.EditedtMessagesLogs.Add(msgLogs);
                    context.SaveChanges();

                    message.Message = newContent;
                    await context.SaveChangesAsync();

                    await Clients.Group(roomName).SendAsync("MessageEdited", messageId, newContent);
                }
              
            }
            catch (Exception ex)
            {
                await LogAsync(userId, "EditMessage ", ex.Message + " InnerException " + (ex.InnerException?.Message ?? string.Empty));
                await Clients.Caller.SendAsync("Error", $"Server error: {ex.Message}");
            }
        }

        // Allows a user to delete their own message in a chat room.
        public async Task DeleteMessage(int messageId, string roomName, string chatUserId = "")
        {
            var userId = "";
            try
            {
                AppHealthTracker.UpdateActivity();
                userId = GetUserId();
                using var scope = _scopeFactory.CreateScope();
                var _context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                if (!string.IsNullOrEmpty(chatUserId))
                {
                    var message = await _context.UsersMessage.FindAsync(messageId);
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
                    await _context.SaveChangesAsync();

                    await Clients.Group(roomName).SendAsync("MessageDeleted", messageId);
                    await Clients.User(chatUserId).SendAsync("UserMessageDeleted", messageId);
                }
                else
                {
                    var message = await _context.ChatMessages.FindAsync(messageId);
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
                    await _context.SaveChangesAsync();
                    await Clients.Group(roomName).SendAsync("MessageDeleted", messageId);
                }
            }
            catch (Exception ex)
            {
                await LogAsync(userId, "DeleteMessage", ex.ToString());
                await Clients.Caller.SendAsync("Error", $"Server error: {ex.Message}");
                throw; // SignalR will catch and send this back to client
            }
        }

        #region Group / Room working

        // List of groups joined by connections (used for group management).
        public static List<string> GroupsJoined { get; set; } = new List<string>();

        // Adds the current user to a SignalR group based on their role.
        [Authorize]
        public async Task JoinGroup(string sender)
        {
            var user = GetUserId();
            var role = (await GetUserRoles(user)).FirstOrDefault();
            if (!GroupsJoined.Contains(Context.ConnectionId + ":" + role))
            {
                GroupsJoined.Add(Context.ConnectionId + ":" + role);
                //do something else
                await Groups.AddToGroupAsync(Context.ConnectionId, role);
            }
        }

        // Adds the current connection to a chat room group.
        public async Task JoinRoom(string roomName)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
            //await Clients.Group(roomName).SendAsync("MessageReceived", "System", $"A new user has joined room: {roomName}");
        }

        // Removes the current connection from a chat room group.
        public async Task LeaveRoom(string roomName)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomName);
            //await Clients.Group(roomName).SendAsync("MessageReceived", "System", $"A user has left room: {roomName}");
        }

        // Sends a message to all users in a chat room and updates unread message status.
        public async Task SendMessageToRoom(string roomName, string user, string message)
        {
            var userId = GetUserId(); // Your method to get current user ID
            try
            {
                AppHealthTracker.UpdateActivity();

                var messageId = 0;
                var messageTime = "";

                var chatMessage = new ChatMessage
                {
                    SenderId = userId,
                    ReceiverId = null,
                    Message = message,
                    GroupName = roomName,
                    CreatedOn = DateTime.Now
                };

                _context.ChatMessages.Add(chatMessage);
                await _context.SaveChangesAsync();
                messageId = chatMessage.Id;
                messageTime = chatMessage.CreatedOn.Value.ToString("dd-MM-yy hh:mm tt");
                // Create unread status entries for other users in group
                var room = await _context.ChatRoom.FirstOrDefaultAsync(r => r.Name == roomName);
                if (room != null)
                {
                    var groupUsers = await _context.GroupUserMapping
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
                    _context.ChatMessageReadStatuses.AddRange(readStatuses);
                    await _context.SaveChangesAsync();
                }

                // Broadcast the message to the room
                var receiver = "";
                var senderId = "";
                var isGroup = true;
                await Clients.Group(roomName).SendAsync("MessageReceived", messageId, user, message, messageTime, senderId, receiver, isGroup);

                // Broadcast updated unread count
                await BroadcastUnreadCount(roomName : roomName);
            }
            catch (Exception ex)
            {
                await LogAsync(userId, "EditMessage ", ex.Message + " InnerException " + (ex.InnerException?.Message ?? string.Empty));
            }
        }

        // Sends a direct message from one user to another and updates unread status.
        public async Task SendMessageToUser(string user, string receiver, string message)
        {
            var userId = GetUserId();
            string senderId = userId;
            try
            {

                if (!string.IsNullOrEmpty(userId))
                {
                    var senderUser = await _userManager.FindByIdAsync(userId);
                    var receiverUser = await _userManager.FindByIdAsync(receiver);

                    var messageId = 0;
                    var messageTime = "";
                    var isGroup = false;

                    if (receiverUser != null)
                    {
                        var chatMessage = new UsersMessage
                        {
                            SenderId = userId,
                            ReceiverId = receiver,
                            Message = message,
                            CreatedOn = DateTime.Now
                        };

                        _context.UsersMessage.Add(chatMessage);
                        await _context.SaveChangesAsync();

                        messageId = chatMessage.Id;
                        messageTime = chatMessage.CreatedOn.Value.ToString("dd-MM-yy hh:mm tt");

                        var readStatuses = new UsersMessageReadStatus
                        {
                            ChatMessageId = chatMessage.Id,
                            SenderId = userId,
                            ReceiverId = receiver,
                            IsRead = false,
                            CreatedOn = DateTime.UtcNow
                        };
                        _context.UsersMessageReadStatus.AddRange(readStatuses);
                        await _context.SaveChangesAsync();
                    }

                    await Clients.User(receiver).SendAsync("MessageReceived", messageId, user, message, messageTime, senderId, receiver, isGroup);

                    await Clients.User(userId).SendAsync("SendMessageUser", messageId, user, message, messageTime, senderId, receiver, isGroup);

                    await BroadcastUnreadCount(SenderId: userId);
                }
            }
            catch (Exception ex)
            {
                await LogAsync(userId, "EditMessage ", ex.Message + " InnerException " + (ex.InnerException?.Message ?? string.Empty));
            }
        }

        // Marks all messages as read for the current user in a specific room.
        public async Task MarkMessagesAsRead(int roomId)
        {
            AppHealthTracker.UpdateActivity();

            var userId = GetUserId();

            var roomName = await _context.ChatRoom
                .Where(r => r.Id == roomId)
                .Select(r => r.Name)
                .FirstOrDefaultAsync();

            if (roomName == null)
                return; // or handle error

            var unreadMessages = await _context.ChatMessageReadStatuses
                .Where(s => s.UserId == userId
                            && s.ChatMessage.GroupName == roomName)
                .ToListAsync();

            foreach (var status in unreadMessages)
            {
                //status.IsRead = true;
                //status.ReadOn = DateTime.Now;
                _context.Remove(status);
            }
            await _context.SaveChangesAsync();
        }

        // Marks all direct messages as read between the current user and another user.
        public async Task P_To_P_MarkMessagesAsRead(string UserId)
        {
            //AppHealthTracker.UpdateActivity();

            var Receiver = GetUserId();

            var roomName = await _context.Users
                .Where(r => r.Id == UserId)
                .FirstOrDefaultAsync();

            if (roomName == null)
                return; // or handle error

            var unreadMessages = await _context.UsersMessageReadStatus
                .Where(s => s.SenderId == UserId && s.ReceiverId == Receiver)
                .ToListAsync();

            foreach (var status in unreadMessages)
            {
                //status.IsRead = true;
                //status.ReadOn = DateTime.Now;
                _context.Remove(status);
            }
            await _context.SaveChangesAsync();
        }

        // Retrieves the count of unread messages for the current user in all rooms and personal chats.
        public async Task GetUnreadMessageCounts()
        {
            var userId = GetUserId();


            var unreadGroupMessages = await (
                from status in _context.ChatMessageReadStatuses
                join chatMsg in _context.ChatMessages on status.ChatMessageId equals chatMsg.Id
                join chatRoom in _context.ChatRoom on chatMsg.GroupName equals chatRoom.Name
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
                from status in _context.UsersMessageReadStatus
                join chatMsg in _context.UsersMessage on status.ChatMessageId equals chatMsg.Id
                join sender in _context.Users on chatMsg.SenderId equals sender.Id
                where status.ReceiverId == userId && status.SenderId == chatMsg.SenderId && !status.IsRead
                group status by new { sender.Id, sender.UserName } into g
                select new
                {
                    RoomId = g.Key.Id,
                    RoomName = g.Key.UserName,
                    Count = g.Count(),
                    isRoom = false
                }
            ).ToListAsync();

            // Combine both results
            unreadGroupMessages.AddRange(unreadPersonalMessages);

            // Send unread count for each room to the **caller** only
            foreach (var room in unreadGroupMessages)
            {
                await Clients.Caller.SendAsync("ReceiveUnreadCount", room.RoomId, room.RoomName, room.Count, room.isRoom);
            }
        }

        // Broadcasts unread message counts to users in a room or for direct messages.
        public async Task BroadcastUnreadCount(string roomName = "" , string SenderId = "")
        {

            var unreadList = new List<(string userId, string roomId, string roomName, int count, bool isroom)>();

            if (!string.IsNullOrEmpty(roomName))
            {
                var room = await _context.ChatRoom.FirstOrDefaultAsync(r => r.Name == roomName);
                if (room == null) return;

                var unreadCounts = await (from status in _context.ChatMessageReadStatuses
                                          join chatMsg in _context.ChatMessages on status.ChatMessageId equals chatMsg.Id
                                          where !status.IsRead && chatMsg.GroupName == roomName
                                          group status by status.UserId into g
                                          select new
                                          {
                                              UserId = g.Key,
                                              roomId = room.Id.ToString(),
                                              roomName = roomName,
                                              Count = g.Count(),
                                              isRoom = true
                                          }).ToListAsync();

                //unreadList.AddRange(unreadCounts);
                unreadList.AddRange(unreadCounts.Select(x => (userId :x.UserId, roomId: x.roomId.ToString(), roomName: roomName, count: x.Count, isroom: x.isRoom)));
            }

            if (!string.IsNullOrEmpty(SenderId))
            {

                var unreadPersonalMessages = await (
                             from status in _context.UsersMessageReadStatus
                             join chatMsg in _context.UsersMessage on status.ChatMessageId equals chatMsg.Id
                             join sender in _context.Users on chatMsg.SenderId equals sender.Id
                             where status.ReceiverId == chatMsg.ReceiverId && 
                             status.SenderId == SenderId && !status.IsRead
                             group status by new { sender.Id, sender.UserName, chatMsg.ReceiverId } into g
                             select new
                             {
                                 UserId = g.Key.ReceiverId,
                                 RoomId = g.Key.Id,
                                 RoomName = g.Key.UserName,
                                 Count = g.Count(),
                                 isRoom = false
                             }
                         ).ToListAsync();

                //unreadList.AddRange(unreadCounts);
                unreadList.AddRange(unreadPersonalMessages.Select(x => (userId: x.UserId, roomId: x.RoomId.ToString(), roomName: x.RoomName, count: x.Count , isroom: x.isRoom)));
            }

            foreach (var userUnread in unreadList)
            {
                await Clients.User(userUnread.userId)
                    .SendAsync("ReceiveUnreadCount", userUnread.roomId, roomName, userUnread.count, userUnread.isroom);
            }
        }

        // Logs chat actions and events to the database.
        public async Task LogAsync(string userId, string actionName, string details = null)
        {
            var log = new ChatLog
            {
                UserId = userId,
                ActionName = actionName,
                Details = details
            };

            _context.ChatLogs.Add(log);
            await _context.SaveChangesAsync();
        }

        #endregion
    }
}
