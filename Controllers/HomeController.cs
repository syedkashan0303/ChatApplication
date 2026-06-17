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
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IHubContext<BasicChatHub> _basicChatHub;
        private readonly ILogger<HomeController> _logger;

        public HomeController(
            IServiceScopeFactory scopeFactory,
            UserManager<ApplicationUser> userManager,
            IHubContext<BasicChatHub> basicChatHub,
            ILogger<HomeController> logger)
        {
            _scopeFactory = scopeFactory;
            _userManager = userManager;
            _basicChatHub = basicChatHub;
            _logger = logger;
        }

        [Authorize]
        public async Task<IActionResult> Index()
        {
            using var scope = _scopeFactory.CreateScope();
            var _db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

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
            using var scope = _scopeFactory.CreateScope();
            var _db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

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
            using var scope = _scopeFactory.CreateScope();
            var _db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

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
            using var scope = _scopeFactory.CreateScope();
            var _db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

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
            using var scope = _scopeFactory.CreateScope();
            var _db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(15));

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

                    var messages = await (
                        from m in _db.ChatMessages.AsNoTracking()
                        join u in _db.Users.AsNoTracking()
                            on m.SenderId equals u.Id into gj
                        from u in gj.DefaultIfEmpty()
                        where m.GroupName == roomName
                        orderby m.Id descending
                        select new
                        {
                            id = m.Id,
                            senderId = m.SenderId,
                            senderName = u != null ? u.UserName : string.Empty,
                            message = m.IsDelete ? "Message deleted" : m.Message,
                            createdOn = m.CreatedOn,
                            messageTime = m.CreatedOn.HasValue
                                ? m.CreatedOn.Value.ToString("dd-MM-yy HH:mm")
                                : "",
                            replyCount = _db.MessageReplies.Count(r => r.ParentMessageId == m.Id && !r.IsDeleted)
                        })
                        .Skip(fromDate)
                        .Take(chunkRecords)
                        .ToListAsync(cts.Token);

                    return Ok(messages);
                }

                // ===========================
                // PRIVATE / ONE-TO-ONE CHAT
                // ===========================
                else
                {
                    if (string.IsNullOrWhiteSpace(receiverId))
                        return BadRequest("receiverId is required.");

                    var messages = await (
                        from m in _db.UsersMessage.AsNoTracking()
                        join u in _db.Users.AsNoTracking()
                            on m.SenderId equals u.Id into gj
                        from u in gj.DefaultIfEmpty()
                        where (m.ReceiverId == receiverId && m.SenderId == currentUserId)
                              || (m.SenderId == receiverId && m.ReceiverId == currentUserId)
                        orderby m.Id descending
                        select new
                        {
                            id = m.Id,
                            senderId = m.SenderId,
                            receiverId = m.ReceiverId,
                            senderName = u != null ? u.UserName : string.Empty,
                            message = m.IsDelete ? "Message deleted" : m.Message,
                            createdOn = m.CreatedOn,
                            messageTime = m.CreatedOn.HasValue
                                ? m.CreatedOn.Value.ToString("dd-MM-yy HH:mm")
                                : ""
                        })
                        .Skip(fromDate)
                        .Take(chunkRecords)
                        .ToListAsync(cts.Token);

                    return Ok(messages);
                }
            }
            catch (OperationCanceledException ex)
            {
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
            finally
            { 
                _db.Dispose();  
            }
        }

        // =====================================================
        // Get Replies for a Group Message
        // =====================================================
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetReplies(
            int parentMessageId,
            int page = 1,
            int pageSize = 50,
            CancellationToken cancellationToken = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var _db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(15));

            try
            {
                if (pageSize > 50) pageSize = 50;

                var replies = await (
                    from r in _db.MessageReplies.AsNoTracking()
                    join u in _db.Users.AsNoTracking()
                        on r.UserId equals u.Id into gj
                    from u in gj.DefaultIfEmpty()
                    where r.ParentMessageId == parentMessageId && !r.IsDeleted
                    orderby r.CreatedOn ascending
                    select new
                    {
                        id = r.Id,
                        userId = r.UserId,
                        userName = u != null ? (u.FullName ?? u.UserName) : string.Empty,
                        replyText = r.ReplyText,
                        createdOn = r.CreatedOn.ToString("dd-MMM-yyyy hh:mm tt")
                    })
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync(cts.Token);

                return Ok(replies);
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "⏳ GetReplies timeout | ParentMessageId={ParentMessageId}", parentMessageId);
                return StatusCode(408, "Request timeout. Please retry.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in GetReplies | ParentMessageId={ParentMessageId}", parentMessageId);
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
            using var scope = _scopeFactory.CreateScope();
            var _db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

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
            using var scope = _scopeFactory.CreateScope();
            var _db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

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
            using var scope = _scopeFactory.CreateScope();
            var _db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

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
            using var scope = _scopeFactory.CreateScope();
            var _db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var userId = GetUserId();

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

            rooms = rooms
                .OrderByDescending(x => x.IsRoom)
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

            ThreadPool.GetAvailableThreads(out int availWorker, out int availIocp);
            ThreadPool.GetMaxThreads(out int maxWorker, out int maxIocp);
            ThreadPool.GetMinThreads(out int minWorker, out int minIocp);

            var diagnostics = new
            {
                status = idleTime > TimeSpan.FromMinutes(1) ? "degraded" : "healthy",
                idleSeconds = (int)idleTime.TotalSeconds,
                activeSignalRConnections = AppHealthTracker.ActiveConnections,
                threadPool = new
                {
                    workerAvailable = availWorker,
                    workerInUse = maxWorker - availWorker,
                    workerMax = maxWorker,
                    workerMin = minWorker,
                    iocpAvailable = availIocp,
                    iocpInUse = maxIocp - availIocp,
                    iocpMax = maxIocp,
                    iocpMin = minIocp
                },
                timestamp = DateTime.UtcNow
            };

            if (idleTime > TimeSpan.FromMinutes(1))
                return StatusCode(500, diagnostics);

            return Ok(diagnostics);
        }

        public class Room
        {
            public string Name { get; set; }
            public string SafeId { get; set; }
            public bool IsRoom { get; set; }
        }
    }
}
