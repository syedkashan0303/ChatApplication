using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SignalRMVC.Areas.Identity.Data;
using SignalRMVC.Models;
using System.Security.Claims;

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
        public override async Task OnConnectedAsync()
        {
            var httpContext = Context.GetHttpContext();
            var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var roles = await GetUserRoles(userId);
            // Now you can use the userId as needed
            await base.OnConnectedAsync();
        }

        public string GetUserId()
        {
            var httpContext = Context.GetHttpContext();
            var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return userId;
        }

        public async Task<IList<string>> GetUserRoles(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            var roles = await _userManager.GetRolesAsync(user);
            return roles;
        }

        //public async Task EditMessage(int messageId, string newContent, string roomName)
        //{
        //    try
        //    {
        //        var userId = GetUserId();
        //        var user = await _userManager.FindByIdAsync(userId);

        //        using (var scope = _scopeFactory.CreateScope())
        //        {
        //            var _context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        //            var message = await _context.ChatMessages.FindAsync(messageId);

        //            if (message == null)
        //            {
        //                await Clients.Caller.SendAsync("Error", "Message not found.");
        //                return;
        //            }

        //            if (message.SenderId != userId)
        //            {
        //                await Clients.Caller.SendAsync("Error", "Unauthorized to edit this message.");
        //                return;
        //            }

        //            message.Message = newContent;
        //            await _context.SaveChangesAsync();

        //            await Clients.Group(roomName).SendAsync("MessageEdited", messageId, newContent);
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        await Clients.Caller.SendAsync("Error", "Server error: " + ex.Message);
        //    }
        //}

        public async Task EditMessage(int messageId, string newContent, string roomName)
        {
            try
            {
                var userId = GetUserId();
                using var scope = _scopeFactory.CreateScope();
                var _context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var message = await _context.ChatMessages.FindAsync(messageId);
                if (message == null)
                {
                    await Clients.Caller.SendAsync("Error", "Message not found.");
                    return;
                }

                if (message.SenderId != userId)
                {
                    await Clients.Caller.SendAsync("Error", "Unauthorized to edit this message.");
                    return;
                }
                var msgLogs = new EditedMessagesLog();
                msgLogs.MessageId = messageId;
                msgLogs.Message = message.Message;
                msgLogs.EditedBy = userId;
                msgLogs.EditedOn = DateTime.Now;
                msgLogs.GroupName = roomName;
                _context.EditedtMessagesLogs.Add(msgLogs);
                await _context.SaveChangesAsync();

                message.Message = newContent;
                await _context.SaveChangesAsync();

                await Clients.Group(roomName).SendAsync("MessageEdited", messageId, newContent);
            }
            catch (Exception ex)
            {
                await Clients.Caller.SendAsync("Error", $"Server error: {ex.Message}");
            }
        }

        public async Task DeleteMessage(int messageId, string roomName)
        {
            try
            {
                var userId = GetUserId();

                using var scope = _scopeFactory.CreateScope();
                var _context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
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
            catch (Exception ex)
            {
                Console.WriteLine("Exception in DeleteMessage: " + ex.ToString());
                await Clients.Caller.SendAsync("Error", $"Server error: {ex.Message}");
                throw; // SignalR will catch and send this back to client
            }
        }

        #region Group / Room working

        public static List<string> GroupsJoined { get; set; } = new List<string>();

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


        public async Task JoinRoom(string roomName)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
            //await Clients.Group(roomName).SendAsync("MessageReceived", "System", $"A new user has joined room: {roomName}");
        }

        public async Task LeaveRoom(string roomName)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomName);
            //await Clients.Group(roomName).SendAsync("MessageReceived", "System", $"A user has left room: {roomName}");
        }

        public async Task SendMessageToRoom(string roomName, string user, string message)
        {
            var messageId = 0;
            var senderUser = await _userManager.FindByNameAsync(user);
            if (senderUser != null)
            {
                var chatMessage = new ChatMessage
                {
                    SenderId = senderUser?.Id,
                    ReceiverId = null, // for group broadcast
                    Message = message,
                    GroupName = roomName,
                    CreatedOn = DateTime.Now
                };

                _context.ChatMessages.Add(chatMessage);
                await _context.SaveChangesAsync();
                messageId = chatMessage.Id;
            }
            await Clients.Group(roomName).SendAsync("MessageReceived", messageId, user, message);
        }
        #endregion

    }
}
