namespace SignalRMVC.Controllers
{
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Identity;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.SignalR;
    using Microsoft.EntityFrameworkCore;
    using SignalRMVC.Areas.Identity.Data;
    using SignalRMVC.Models;
    using System.Security.Claims;

    public class HomeController : Controller
    {
        private readonly AppDbContext _db;

        private readonly UserManager<ApplicationUser> _userManager;

        private readonly IHubContext<BasicChatHub> _basicChatHub;

        public HomeController(
        AppDbContext context,
        UserManager<ApplicationUser> userManager,
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

        [HttpPost("SendMessageToGroup")]
        [Authorize]
        public async Task<IActionResult> SendMessageToGroup([FromForm] string user, [FromForm] string room, [FromForm] string message)
        {
            var senderUser = await _userManager.FindByNameAsync(user);
            if (senderUser != null)
            {
                var chatMessage = new ChatMessage
                {
                    SenderId = senderUser?.Id,
                    ReceiverId = null, // for group broadcast
                    Message = message,
                    GroupName = room,
                    CreatedOn = DateTime.Now
                };

                _db.ChatMessages.Add(chatMessage);
                await _db.SaveChangesAsync();
                await _basicChatHub.Clients.Group(room).SendAsync("MessageReceived", senderUser.UserName, message);
            }
            return Ok();
        }

        [HttpGet]
        public IActionResult GetMessagesByRoom(string roomName, int skipRecords = 0, int chunkRecords = 100)
        {
            var now = DateTime.UtcNow;
            var fromDate = skipRecords * chunkRecords;
            var messages = _db.ChatMessages
              .Where(m => m.GroupName == roomName && !m.IsDelete)
              .OrderByDescending(m => m.Id).Skip(fromDate).Take(chunkRecords)
              .Join(
                  _db.Users,
                  message => message.SenderId,
                  user => user.Id,
                  (message, user) => new
                  {
                      id = message.Id,
                      sender = user.UserName, // Or use user.UserName or FullName if you have it
                      message = message.Message,
                      createdOn = message.CreatedOn,
                      messageTime = message.CreatedOn != null ? message.CreatedOn.Value.ToString("hh:mm tt") : DateTime.Now.ToString("hh : mm tt")
                  }
              )
              .ToList().OrderBy(x => x.id);
            return Ok(messages);
        }

        [HttpPost]
        public async Task<IActionResult> EditMessage(int id, string newContent)
        {
            if (string.IsNullOrWhiteSpace(newContent))
                return BadRequest("Message cannot be empty.");

            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var message = await _db.ChatMessages.FindAsync(id);

            if (message == null)
                return NotFound("Message not found.");

            if (message.SenderId != userId)
                return Forbid("You can only edit your own messages.");

            message.Message = newContent;
            await _db.SaveChangesAsync();

            return Ok();
        }

        [HttpGet]
        public IActionResult GetTheme()
        {
            var userId = GetUserId();

            var user = _db.Users.FirstOrDefault(x => x.Id == userId);
            if (user != null)
            {
                return Ok(user.IsDarkTheme);
            }
            return Ok("false");
        }

        [HttpGet]
        public IActionResult UpdateTheme()
        {
            var userId = GetUserId();

            var user = _db.Users.FirstOrDefault(x => x.Id == userId);
            if (user != null)
            {
                user.IsDarkTheme = user.IsDarkTheme ? false : true;
                _db.Update(user);
                _db.SaveChanges();
            }
            return Ok();
        }

        [HttpGet]
        public async Task<IActionResult> GetRooms()
        {
            var userId = GetUserId();

            // Get Group IDs for current user
            var groupUserList = await _db.GroupUserMapping
                .Where(x => x.Active && x.UserId == userId)
                .Select(x => x.GroupId)
                .ToListAsync();

            // Fetch group names and also generate encoded ID-safe names
            var rooms = await _db.ChatRoom
                .Where(x => !x.isDelete && groupUserList.Contains(x.Id))
                .Select(r => new
                {
                    Name = r.Name,
                    SafeId = r.Id // or use your own sanitizer if needed
                })
                .ToListAsync();

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
    }
}
