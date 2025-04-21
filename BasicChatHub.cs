using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using SignalRMVC.Models;
using System.Security.Claims;

namespace SignalRMVC
{
    public class BasicChatHub : Hub
    {
        private readonly UserManager<ApplicationUser> _userManager;
        public BasicChatHub(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
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
            await Clients.Group(roomName).SendAsync("MessageReceived", user, message);
        }


        #endregion

    }
}
