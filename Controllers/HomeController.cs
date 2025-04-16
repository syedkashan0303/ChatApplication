using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SignalRMVC.Areas.Identity.Data;
using SignalRMVC.Models;
using System.Security.Claims;

namespace SignalRMVC.Controllers
{

    public class HomeController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IHubContext<BasicChatHub> _basicChatHub;
        private readonly UserManager<IdentityUser> _userManager;

        public HomeController(
            AppDbContext context,
            UserManager<IdentityUser> userManager,
            IHubContext<BasicChatHub> basicChatHub)
        {
            _db = context;
            _userManager = userManager;
            _basicChatHub = basicChatHub;
        }
        [Authorize]
        public async Task<IActionResult> Index()
        {

            var model = new RoleViewModel();
            var user = await _userManager.GetUserAsync(User);
            if (user is not null)
            {
                var roles1 = await _db.UserRoles
    .Where(ur => ur.UserId == user.Id)
    .ToListAsync();
                var roles = await _userManager.GetRolesAsync(user);
                model.UserRoles = roles;
            }

            return View(model);

        }

        [HttpGet("SendMessageToAll")]
        [Authorize]
        public async Task<IActionResult> SendMessageToAll(string user, string message)
        {

            var senderUser = await _userManager.FindByNameAsync(user);

            var chatMessage = new ChatMessage
            {
                SenderId = senderUser?.Id,
                ReceiverId = null, // for broadcast
                Message = message,
                GroupName = null,
                CreatedOn = DateTime.Now
            };

            _db.ChatMessages.Add(chatMessage);
            await _db.SaveChangesAsync();

            await _basicChatHub.Clients.All.SendAsync("MessageReceived", user, message);
            return Ok();
        }

        [HttpGet("SendMessageToReceiver")]
        [Authorize]
        public async Task<IActionResult> SendMessageToReceiver(string sender, string receiver, string message)
        {
            var userId = _db.Users.FirstOrDefault(u => u.Email.ToLower() == receiver.ToLower())?.Id;

            if (!string.IsNullOrEmpty(userId))
            {
                var senderUser = await _userManager.FindByNameAsync(sender);
                var receiverUser = await _userManager.FindByEmailAsync(receiver);

                if (receiverUser != null)
                {
                    var chatMessage = new ChatMessage
                    {
                        SenderId = senderUser?.Id,
                        ReceiverId = receiverUser.Id,
                        Message = message
                    };

                    _db.ChatMessages.Add(chatMessage);
                    await _db.SaveChangesAsync();
                }


                await _basicChatHub.Clients.User(userId).SendAsync("MessageReceived", sender, message);
            }
            return Ok();
        }

        [HttpGet("SendMessageToGroup")]
        [Authorize]
        public async Task SendMessageToGroup(string user,string room, string message)
        {
            var senderUser = await _userManager.FindByNameAsync(user);
            var chatMessage = new ChatMessage
            {
                SenderId = senderUser?.Id,
                ReceiverId = null, // for broadcast
                Message = message,
                GroupName = room,
                CreatedOn = DateTime.Now
            };

            _db.ChatMessages.Add(chatMessage);
            await _db.SaveChangesAsync();
            await _basicChatHub.Clients.Group(room).SendAsync("MessageReceived", senderUser.UserName, message);


            //var role = (await GetUserRoles(user)).FirstOrDefault();


            //if (!string.IsNullOrEmpty(role))
            //{
            //}

        }
        [HttpPost]
        public async Task<IActionResult> CreateRoom([FromForm] string roomName)
        {
            if (!string.IsNullOrWhiteSpace(roomName))
            {
                var exists = await _db.ChatRoom.AnyAsync(r => r.Name == roomName);
                if (!exists)
                {
                    _db.ChatRoom.Add(new ChatRoom { Name = roomName });
                    await _db.SaveChangesAsync();
                }
            }

            return Ok();
        }

        [HttpGet]
        public async Task<IActionResult> GetMessagesByRoom(string roomName)
        {
            var messages = await _db.ChatMessages
                .Where(m => m.GroupName == roomName)
                .OrderBy(m => m.CreatedOn)
                .Join(
                    _db.Users,
                    message => message.SenderId,
                    user => user.Id,
                    (message, user) => new
                    {
                        sender = user.UserName, // Or use user.UserName or FullName if you have it
                        message = message.Message,
                        createdOn = message.CreatedOn
                    }
                )
                .ToListAsync();


            return Json(messages);
        }

        [HttpGet]
        public async Task<IActionResult> GetRooms()
        {
            var rooms = await _db.ChatRoom.Select(r => r.Name).ToListAsync();
            return Json(rooms);
        }
        private string GetUserId()
        {
            var userId = HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return userId;
        }

        private async Task<IList<string>> GetUserRoles(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            var roles = await _userManager.GetRolesAsync(user);
            return roles;
        }


        public async Task<IActionResult> CreateGroup()
        {
            var model = new RoleViewModel();
            var user = await _userManager.GetUserAsync(User);
            if (user is not null)
            {
                var roles1 = await _db.UserRoles.Where(ur => ur.UserId == user.Id).ToListAsync();
                var roles = await _userManager.GetRolesAsync(user);
                model.UserRoles = roles;
            }
            return View(model);
        }

    }
}
