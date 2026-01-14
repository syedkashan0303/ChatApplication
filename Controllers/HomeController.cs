namespace SignalRMVC.Controllers
{
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Identity;
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.SignalR;
    using Microsoft.EntityFrameworkCore;
    using SignalRMVC.Areas.Identity.Data;
    using SignalRMVC.CustomClasses;
    using SignalRMVC.Models;
    using System.Security.Claims;
    using System.Threading;

    public class HomeController : Controller
    {
        private readonly AppDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IHubContext<BasicChatHub> _basicChatHub;
        private readonly ILogger<HomeController> _logger;

        public HomeController(
            AppDbContext context,
            UserManager<ApplicationUser> userManager,
            IHubContext<BasicChatHub> basicChatHub,
            ILogger<HomeController> logger)
        {
            _db = context;
            _userManager = userManager;
            _basicChatHub = basicChatHub;
            _logger = logger;
        }

        [Authorize]
        public async Task<IActionResult> Index()
        {
            var model = new RoleViewModel();
            var user = await _userManager.GetUserAsync(User);

            if (user is not null)
            {
                var roles = await _userManager.GetRolesAsync(user);
                model.UserRoles = roles;
            }

            return View(model);
        }

        // =====================================================
        // Send message to all
        // =====================================================
        [HttpGet("SendMessageToAll")]
        [Authorize]
        public async Task<IActionResult> SendMessageToAll(string user, string message)
        {
            var senderUser = await _userManager.FindByNameAsync(user);

            var chatMessage = new ChatMessage
            {
                SenderId = senderUser?.Id,
                ReceiverId = null,
                Message = message,
                GroupName = null,
                CreatedOn = DateTime.Now
            };

            _db.ChatMessages.Add(chatMessage);
            await _db.SaveChangesAsync();

            await _basicChatHub.Clients.All.SendAsync("MessageReceived", user, message);
            return Ok();
        }

        // =====================================================
        // Send message to receiver
        // =====================================================
        [HttpGet("SendMessageToReceiver")]
        [Authorize]
        public async Task<IActionResult> SendMessageToReceiver(string sender, string receiver, string message)
        {
            var userId = await _db.Users
                .Where(u => u.Email.ToLower() == receiver.ToLower())
                .Select(u => u.Id)
                .FirstOrDefaultAsync();

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
                        Message = message,
                        CreatedOn = DateTime.Now
                    };

                    _db.ChatMessages.Add(chatMessage);
                    await _db.SaveChangesAsync();
                }

                await _basicChatHub.Clients.User(userId).SendAsync("MessageReceived", sender, message);
            }

            return Ok();
        }

        // =====================================================
        // Send message to group
        // =====================================================
        [HttpPost("SendMessageToGroup")]
        [Authorize]
        public async Task<IActionResult> SendMessageToGroup([FromForm] string user, [FromForm] string room, [FromForm] string message)
        {
            var senderUser = await _userManager.FindByNameAsync(user);

            if (senderUser != null)
            {
                var chatMessage = new ChatMessage
                {
                    SenderId = senderUser.Id,
                    ReceiverId = null,
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

        // =====================================================
        // Get Messages By Room / Personal (with timeout)
        // =====================================================
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetMessagesByRoom(
            string roomName,
            int skipRecords = 0,
            int chunkRecords = 10,
            bool isRoom = false,
            string receiverId = "",
            CancellationToken cancellationToken = default)
        {
            // ✅ Linked token + timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(30));

            try
            {
                var fromDate = skipRecords * chunkRecords;
                var currentUserId = GetUserId();

                if (string.IsNullOrEmpty(currentUserId))
                    return Unauthorized("User not authenticated.");

                // ===========================
                // GROUP / ROOM CHAT
                // ===========================
                if (isRoom)
                {
                    if (string.IsNullOrWhiteSpace(roomName))
                        return BadRequest("roomName is required.");

                    var messages = await _db.ChatMessages
                        .AsNoTracking()
                        .Where(m => m.GroupName == roomName)
                        .OrderByDescending(m => m.Id)
                        .Skip(fromDate)
                        .Take(chunkRecords)
                        .Join(
                            _db.Users.AsNoTracking(),
                            m => m.SenderId,
                            u => u.Id,
                            (m, u) => new
                            {
                                id = m.Id,
                                sender = u.UserName,
                                message = m.IsDelete ? "Message deleted" : m.Message,
                                createdOn = m.CreatedOn,
                                messageTime = m.CreatedOn.HasValue
                                    ? m.CreatedOn.Value.ToString("dd-MM-yy HH:mm")
                                    : ""
                            }
                        )
                        .ToListAsync(cts.Token);

                    return Ok(messages.OrderByDescending(x => x.id));
                }

                // ===========================
                // PRIVATE / ONE-TO-ONE CHAT
                // ===========================
                else
                {
                    if (string.IsNullOrWhiteSpace(receiverId))
                        return BadRequest("receiverId is required.");

                    var messages = await _db.UsersMessage
                        .AsNoTracking()
                        .Where(m =>
                            (m.ReceiverId == receiverId && m.SenderId == currentUserId) ||
                            (m.SenderId == receiverId && m.ReceiverId == currentUserId)
                        )
                        .OrderByDescending(m => m.Id)
                        .Skip(fromDate)
                        .Take(chunkRecords)
                        .Join(
                            _db.Users.AsNoTracking(),
                            m => m.SenderId,
                            u => u.Id,
                            (m, u) => new
                            {
                                id = m.Id,
                                sender = u.UserName,
                                message = m.IsDelete ? "Message deleted" : m.Message,
                                createdOn = m.CreatedOn,
                                messageTime = m.CreatedOn.HasValue
                                    ? m.CreatedOn.Value.ToString("dd-MM-yy HH:mm")
                                    : ""
                            }
                        )
                        .ToListAsync(cts.Token);

                    return Ok(messages.OrderByDescending(x => x.id));
                }
            }
            catch (OperationCanceledException ex)
            {
                // ✅ Timeout / cancelled request
                _logger.LogWarning(ex,
                    "⏳ GetMessagesByRoom timeout/cancelled | Room={RoomName} | Skip={Skip} | Chunk={Chunk} | IsRoom={IsRoom} | Receiver={ReceiverId}",
                    roomName, skipRecords, chunkRecords, isRoom, receiverId);

                return StatusCode(408, "Request timeout. Please retry.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "❌ Error in GetMessagesByRoom | Room={RoomName} | Skip={Skip} | Chunk={Chunk} | IsRoom={IsRoom} | Receiver={ReceiverId}",
                    roomName, skipRecords, chunkRecords, isRoom, receiverId);

                return StatusCode(500, "Something went wrong");
            }
        }

        // =====================================================
        // Edit Message (Controller)
        // =====================================================
        [HttpPost]
        [Authorize]
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

        // =====================================================
        // Theme
        // =====================================================
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetTheme()
        {
            var userId = GetUserId();

            var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId);
            if (user != null)
            {
                return Ok(user.IsDarkTheme);
            }

            return Ok(false);
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> UpdateTheme()
        {
            var userId = GetUserId();

            var user = await _db.Users.FirstOrDefaultAsync(x => x.Id == userId);
            if (user != null)
            {
                user.IsDarkTheme = !user.IsDarkTheme;
                await _db.SaveChangesAsync();
            }

            return Ok();
        }

        // =====================================================
        // Get Rooms
        // =====================================================
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetRooms()
        {
            var userId = GetUserId();

            // Get Group IDs for current user
            var groupUserList = await _db.GroupUserMapping
                .AsNoTracking()
                .Where(x => x.Active && x.UserId == userId)
                .Select(x => x.GroupId)
                .ToListAsync();

            var users = await _db.Users
                .AsNoTracking()
                .Where(x => !x.IsDeleted && x.Id != userId)
                .Select(x => new { x.Id, x.UserName })
                .ToListAsync();

            var rooms = await _db.ChatRoom
                .AsNoTracking()
                .Where(x => !x.isDelete && groupUserList.Contains(x.Id))
                .Select(r => new Room
                {
                    Name = r.Name,
                    SafeId = r.Id.ToString(),
                    IsRoom = true
                })
                .ToListAsync();

            rooms.AddRange(
                users.Select(u => new Room
                {
                    Name = u.UserName,
                    SafeId = u.Id,
                    IsRoom = false
                })
            );

            // ✅ Fix: OrderBy must assign result
            rooms = rooms
                .OrderByDescending(x => x.IsRoom) // Groups first
                .ThenBy(x => x.Name)
                .ToList();

            return Json(rooms);
        }

        // =====================================================
        // Helpers
        // =====================================================
        private string GetUserId()
        {
            return HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }

        [HttpGet("/Home/ping")]
        public IActionResult Ping()
        {
            var idleTime = DateTime.UtcNow - AppHealthTracker.LastActivityTime;

            if (idleTime > TimeSpan.FromMinutes(1))
            {
                return StatusCode(500, "App is idle or frozen");
            }

            return Ok("Healthy " + idleTime);
        }

        public class Room
        {
            public string Name { get; set; }
            public string SafeId { get; set; }
            public bool IsRoom { get; set; }
        }
    }
}
