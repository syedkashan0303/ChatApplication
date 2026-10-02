namespace SignalRMVC.Controllers
{
    using Microsoft.AspNetCore.Authorization;
    using Microsoft.AspNetCore.Identity;
    using Microsoft.AspNetCore.Mvc;
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
        private readonly ILogger<HomeController> _logger;

        public HomeController(
            IServiceScopeFactory scopeFactory,
            UserManager<ApplicationUser> userManager,
            ILogger<HomeController> logger)
        {
            _scopeFactory = scopeFactory;
            _userManager = userManager;
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
                        join reply in _db.ChatMessages.AsNoTracking()
                            on m.ReplyToMessageId equals reply.Id into replyJoin
                        from reply in replyJoin.DefaultIfEmpty()
                        join replySender in _db.Users.AsNoTracking()
                            on reply.SenderId equals replySender.Id into replySenderJoin
                        from replySender in replySenderJoin.DefaultIfEmpty()
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
                            replyToMessageId = m.ReplyToMessageId,
                            replyToMessageSender = reply == null
                                ? string.Empty
                                : (replySender != null ? (replySender.UserName ?? replySender.FullName ?? string.Empty) : string.Empty),
                            replyToMessageText = reply == null
                                ? null
                                : (reply.IsDelete ? "Message deleted" : reply.Message),
                            replyToMessageDeleted = reply == null || reply.IsDelete,
                            replyMessage = reply == null
                                ? null
                                : new
                                {
                                    id = reply.Id,
                                    message = reply.IsDelete ? "Message deleted" : reply.Message,
                                    senderName = replySender != null ? (replySender.UserName ?? replySender.FullName ?? string.Empty) : string.Empty
                                }
                        })
                        .Skip(fromDate)
                        .Take(chunkRecords)
                        .ToListAsync(cts.Token);

                    // One extra query for all reactions of this page (no per-message round trips)
                    var messageIds = messages.Select(m => m.id).ToList();
                    var reactionRows = await _db.ChatMessageReactions
                        .AsNoTracking()
                        .Where(r => messageIds.Contains(r.ChatMessageId))
                        .Select(r => new { r.ChatMessageId, userName = r.User.UserName, r.Emoji })
                        .ToListAsync(cts.Token);

                    var reactionsByMessage = reactionRows
                        .GroupBy(r => r.ChatMessageId)
                        .ToDictionary(g => g.Key, g => g.Select(r => new { userName = r.userName, emoji = r.Emoji }).ToList());

                    var result = messages.Select(m => new
                    {
                        m.id,
                        m.senderId,
                        m.senderName,
                        m.message,
                        m.createdOn,
                        m.messageTime,
                        m.replyToMessageId,
                        m.replyToMessageSender,
                        m.replyToMessageText,
                        m.replyToMessageDeleted,
                        m.replyMessage,
                        reactions = reactionsByMessage.TryGetValue(m.id, out var rx) ? rx : null
                    }).ToList();

                    return Ok(result);
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

            // Execute sp_SortingChatUser to get conversation order for Category 1 users
            var sortedUserIds = new List<string>();
            try
            {
                var connectionString = _db.Database.GetConnectionString();
                if (!string.IsNullOrEmpty(connectionString))
                {
                    await using var conn = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
                    await conn.OpenAsync();

                    // Execute stored procedure
                    await using var cmd = new Microsoft.Data.SqlClient.SqlCommand("sp_SortingChatUser", conn)
                    {
                        CommandType = System.Data.CommandType.StoredProcedure
                    };
                    cmd.Parameters.AddWithValue("@UserId", userId ?? string.Empty);

                    await using var reader = await cmd.ExecuteReaderAsync();
                    while (await reader.ReadAsync())
                    {
                        if (!reader.IsDBNull(0))
                        {
                            sortedUserIds.Add(reader.GetString(0));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "sp_SortingChatUser execution warning for UserId={UserId}", userId);
            }

            // Partition users into Category 1 (active conversations in SP order) and Category 2 (no conversations, alphabetical)
            var userDict = users.ToDictionary(u => u.Id, u => u);

            var category1Rooms = new List<Room>();
            foreach (var sortedId in sortedUserIds)
            {
                if (userDict.TryGetValue(sortedId, out var activeUser))
                {
                    category1Rooms.Add(new Room
                    {
                        Name = activeUser.UserName ?? string.Empty,
                        SafeId = activeUser.Id,
                        IsRoom = false
                    });
                    userDict.Remove(sortedId);
                }
            }

            var category2Rooms = userDict.Values
                .OrderBy(u => u.UserName)
                .Select(u => new Room
                {
                    Name = u.UserName ?? string.Empty,
                    SafeId = u.Id,
                    IsRoom = false
                })
                .ToList();

            var rooms = await _db.ChatRoom
                .AsNoTracking()
                .Where(x => !x.isDelete && groupUserList.Contains(x.Id))
                .Select(r => new Room
                {
                    Name = r.Name,
                    SafeId = r.Id.ToString(),
                    IsRoom = true
                })
                .OrderBy(r => r.Name)
                .ToListAsync();

            rooms.AddRange(category1Rooms);
            rooms.AddRange(category2Rooms);

            return Json(rooms);
        }

        // =====================================================
        // Helpers
        // =====================================================
        private string GetUserId()
        {
            return HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }


        [AllowAnonymous]
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
