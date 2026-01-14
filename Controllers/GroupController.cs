using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SignalRMVC.Areas.Identity.Data;
using SignalRMVC.CustomClasses;
using SignalRMVC.Models;
using System.Security.Claims;

namespace SignalRMVC.Controllers
{
    public class GroupController : Controller
    {
        private readonly AppDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ILogger<GroupController> _logger;

        public GroupController(AppDbContext context, UserManager<ApplicationUser> userManager, ILogger<GroupController> logger)
        {
            _context = context;
            _userManager = userManager;
            _logger = logger;
        }

        #region Group

        // GET: Group
        [AdminOnly]
        public async Task<IActionResult> Index()
        {
            var groups = await _context.ChatRoom
                .AsNoTracking()
                .Where(x => !x.isDelete)
                .ToListAsync();

            return View(groups);
        }

        [AdminOnly]
        [HttpGet]
        public async Task<JsonResult> Details(int id)
        {
            var chatRoom = await _context.ChatRoom.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);

            if (chatRoom == null)
                return Json(new { success = false, message = "Chat room not found." });

            return Json(new { success = true, Name = chatRoom.Name });
        }

        [HttpPost]
        [AdminOnly]
        public async Task<JsonResult> EditInline([FromBody] ChatRoom model)
        {
            try
            {
                if (model == null || model.Id <= 0 || string.IsNullOrWhiteSpace(model.Name))
                    return Json(new { success = false, message = "Invalid group data." });

                var chatRoom = await _context.ChatRoom.FirstOrDefaultAsync(x => x.Id == model.Id);

                if (chatRoom == null)
                    return Json(new { success = false, message = "Chat room not found." });

                // ✅ Rename group messages also (Async)
                var chatsByRoomName = await _context.ChatMessages
                    .Where(x => !string.IsNullOrEmpty(x.GroupName) && x.GroupName == chatRoom.Name)
                    .ToListAsync();

                if (chatsByRoomName.Any())
                {
                    chatsByRoomName.ForEach(x => x.GroupName = model.Name);
                }

                chatRoom.Name = model.Name;
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Changes saved successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ EditInline failed | GroupId={GroupId}", model?.Id);
                return Json(new { success = false, message = "Error saving changes: " + ex.Message });
            }
        }

        [AdminOnly]
        [HttpPost]
        public async Task<IActionResult> CreateGroup([FromBody] ChatRoom model)
        {
            try
            {
                if (model == null || string.IsNullOrWhiteSpace(model.Name))
                    return BadRequest(new { success = false, message = "Group name is required." });

                // ✅ Duplicate group name check
                var alreadyExists = await _context.ChatRoom.AnyAsync(x => !x.isDelete && x.Name == model.Name);
                if (alreadyExists)
                    return BadRequest(new { success = false, message = "Group name already exists." });

                var group = new ChatRoom
                {
                    Name = model.Name.Trim(),
                    isDelete = false,
                    CreatedOn = DateTime.Now
                };

                _context.ChatRoom.Add(group);
                await _context.SaveChangesAsync();

                var userId = GetUserId();

                var groupUserMapping = new GroupUserMapping
                {
                    UserId = userId,
                    GroupId = group.Id,
                    Active = true,
                    AddedBy = userId,
                    RemovedBy = "0",
                    CreatedOn = DateTime.Now
                };

                _context.GroupUserMapping.Add(groupUserMapping);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Group created successfully!",
                    groupId = group.Id
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ CreateGroup failed");
                return StatusCode(500, new { success = false, message = $"Error creating group: {ex.Message}" });
            }
        }

        [HttpPost]
        [AdminOnly]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var chatRoom = await _context.ChatRoom.FirstOrDefaultAsync(x => x.Id == id);

                if (chatRoom == null)
                    return Json(new { success = false, message = "Chat room not found." });

                // ✅ Mark all messages deleted (Async)
                var chatsByRoomName = await _context.ChatMessages
                    .Where(x => !string.IsNullOrEmpty(x.GroupName) && x.GroupName == chatRoom.Name)
                    .ToListAsync();

                if (chatsByRoomName.Any())
                {
                    chatsByRoomName.ForEach(x => x.IsDelete = true);
                }

                chatRoom.isDelete = true;
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Chat room deleted successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Delete group failed | GroupId={GroupId}", id);
                return Json(new { success = false, message = "Error deleting chat room: " + ex.Message });
            }
        }

        [AdminOnly]
        [HttpGet]
        public async Task<IActionResult> UserChatHistory(string id)
        {
            var chatList = new List<ChatHistory>();

            try
            {
                var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);

                if (user != null)
                {
                    chatList = await _context.ChatMessages
                        .AsNoTracking()
                        .Where(x => x.SenderId == id)
                        .OrderByDescending(x => x.CreatedOn)
                        .Select(u => new ChatHistory
                        {
                            Id = u.Id,
                            Chat = u.Message,
                            GroupName = u.GroupName,
                            Date = u.CreatedOn.HasValue ? u.CreatedOn.Value.ToString("dd-MM-yyyy") : ""
                        })
                        .ToListAsync();
                }

                return Json(chatList);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ UserChatHistory failed | UserId={UserId}", id);
                return Json(chatList);
            }
        }

        #endregion

        #region User

        [AdminOnly]
        public async Task<IActionResult> UserList()
        {
            var currentUserId = GetUserId();

            var userRoles = from role in _context.Roles
                            join userRole in _context.UserRoles on role.Id equals userRole.RoleId
                            select new
                            {
                                UserId = userRole.UserId,
                                RoleName = role.Name
                            };

            var userList = await _userManager.Users
                .AsNoTracking()
                .Where(x => !x.IsDeleted)
                .Select(u => new UserViewModel
                {
                    Id = u.Id,
                    UserName = u.FullName,
                    Email = u.Email,
                    LoginName = u.UserName,
                    RoleName = userRoles.Any() ? userRoles.FirstOrDefault(x => x.UserId == u.Id).RoleName : "",
                    IsCurrentUser = (currentUserId == u.Id),
                    PhoneNumber = u.PhoneNumber,
                    LockoutEnd = u.LockoutEnd,
                    LockoutEnabled = u.LockoutEnabled,
                    AccessFailedCount = u.AccessFailedCount
                })
                .ToListAsync();

            return View(userList);
        }

        [HttpPost]
        [AdminOnly]
        public async Task<IActionResult> LockUser(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return Json(new { success = false, message = "User not found." });

            user.LockoutEnabled = true;
            user.LockoutEnd = DateTimeOffset.Now.AddYears(10);

            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "User locked" });
        }

        [HttpPost]
        [AdminOnly]
        public async Task<IActionResult> UnlockUser(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
                return Json(new { success = false, message = "User not found." });

            user.LockoutEnabled = false;
            user.LockoutEnd = null;

            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "User unlocked" });
        }

        [HttpPost]
        [AdminOnly]
        public async Task<IActionResult> DeleteUser(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
                return Json(new { success = false, message = "User not found." });

            user.IsDeleted = true;
            _context.Update(user);

            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "User deleted" });
        }

        [AdminOnly]
        public async Task<IActionResult> AddUser()
        {
            var model = new UserModal();

            var roles = await _context.Roles.AsNoTracking().ToListAsync();
            foreach (var item in roles)
            {
                model.UserRoles.Add(new SelectListItem { Text = item.Name, Value = item.Id });
            }

            return View(model);
        }

        [HttpPost]
        [AdminOnly]
        public async Task<IActionResult> AddUser(UserModal model)
        {
            ModelState.Remove("Id");

            if (!ModelState.IsValid)
            {
                model.UserRoles = await GetUserRoles();
                return View(model);
            }

            var existingUser = await _userManager.FindByEmailAsync(model.Email);
            if (existingUser != null)
            {
                ModelState.AddModelError("Email", "Email address is already in use");
                model.UserRoles = await GetUserRoles();
                return View(model);
            }

            var existingUserName = await _userManager.FindByNameAsync(model.NormalizedUserName);
            if (existingUserName != null)
            {
                ModelState.AddModelError("NormalizedUserName", "User Name already exists");
                model.UserRoles = await GetUserRoles();
                return View(model);
            }

            var user = new ApplicationUser
            {
                FullName = model.UserName,
                UserName = model.NormalizedUserName,
                NormalizedUserName = model.Email,
                Email = model.Email,
                EmailConfirmed = true,
                NormalizedEmail = model.Email,
                PhoneNumber = "",
                TwoFactorEnabled = false,
                LockoutEnabled = true,
                IsDeleted = false
            };

            var result = await _userManager.CreateAsync(user, model.PasswordHash);

            if (!result.Succeeded)
            {
                foreach (var error in result.Errors)
                {
                    ModelState.AddModelError(string.Empty, error.Description);
                }

                model.UserRoles = await GetUserRoles();
                return View(model);
            }

            if (!string.IsNullOrEmpty(model.RoleId))
            {
                var role = await _context.Roles.FirstOrDefaultAsync(x => x.Id == model.RoleId);
                if (role != null)
                {
                    var roleResult = await _userManager.AddToRoleAsync(user, role.Name);

                    if (!roleResult.Succeeded)
                    {
                        foreach (var error in roleResult.Errors)
                        {
                            ModelState.AddModelError(string.Empty, $"Role assignment failed: {error.Description}");
                        }

                        model.UserRoles = await GetUserRoles();
                        return View(model);
                    }
                }
            }

            TempData["SuccessMessage"] = "User created successfully";
            return RedirectToAction("UserList");
        }

        [AdminOnly]
        [HttpGet]
        public async Task<IActionResult> EditUser(string id)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
            if (user == null) return NotFound();

            var userRole = await _context.UserRoles.FirstOrDefaultAsync(x => x.UserId == user.Id);

            var model = new UserModal
            {
                Id = user.Id,
                UserName = user.FullName,
                PhoneNumber = user.PhoneNumber,
                Email = user.Email,
                RoleId = userRole != null ? userRole.RoleId : ""
            };

            // ❌ Removed .Result (deadlock)
            model.UserRoles.AddRange(await GetUserRoles());

            return View(model);
        }

        [HttpPost]
        [AdminOnly]
        public async Task<IActionResult> EditUser(UserModal model)
        {
            if (!ModelState.IsValid)
            {
                model.UserRoles = await GetUserRoles();
                return View(model);
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == model.Id);
            if (user == null) return NotFound();

            user.FullName = model.UserName;
            user.PhoneNumber = model.PhoneNumber;

            // ✅ Password update properly (NO .Result)
            if (!string.IsNullOrEmpty(model.PasswordHash))
            {
                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                var passwordResult = await _userManager.ResetPasswordAsync(user, token, model.PasswordHash);

                if (!passwordResult.Succeeded)
                {
                    foreach (var error in passwordResult.Errors)
                        ModelState.AddModelError("", error.Description);

                    model.UserRoles = await GetUserRoles();
                    return View(model);
                }
            }

            // Update role mapping
            var oldRole = await _context.UserRoles.FirstOrDefaultAsync(x => x.UserId == user.Id);

            if (oldRole != null && oldRole.RoleId != model.RoleId)
            {
                _context.UserRoles.Remove(oldRole);
            }

            var roleEntity = await _context.Roles.FirstOrDefaultAsync(x => x.Id == model.RoleId);
            if (roleEntity != null)
            {
                var alreadyHasRole = await _userManager.IsInRoleAsync(user, roleEntity.Name);
                if (!alreadyHasRole)
                    await _userManager.AddToRoleAsync(user, roleEntity.Name);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction("UserList");
        }

        private async Task<List<SelectListItem>> GetUserRoles()
        {
            var roles = await _context.Roles.AsNoTracking().ToListAsync();

            return roles.Select(r => new SelectListItem
            {
                Value = r.Id,
                Text = r.Name
            }).ToList();
        }

        #endregion

        #region Group User Mapping

        [AdminOnly]
        [HttpGet]
        public async Task<IActionResult> UserListInGroup(int id)
        {
            try
            {
                var currentUserId = GetUserId();

                var groupUserList = await _context.GroupUserMapping
                    .AsNoTracking()
                    .Where(x => x.Active && x.GroupId == id)
                    .Select(x => x.UserId)
                    .ToListAsync();

                var userRoles = from role in _context.Roles
                                join userRole in _context.UserRoles on role.Id equals userRole.RoleId
                                select new
                                {
                                    UserId = userRole.UserId,
                                    RoleName = role.Name
                                };

                var userList = await _context.Users
                    .AsNoTracking()
                    .Select(u => new UserViewModel
                    {
                        Id = u.Id,
                        UserName = u.FullName,
                        Email = u.Email,
                        RoleName = userRoles.Any() ? userRoles.FirstOrDefault(x => x.UserId == u.Id).RoleName : "",
                        IsCurrentUser = (currentUserId == u.Id),
                        PhoneNumber = u.PhoneNumber,
                        IsAlreadyInGroup = groupUserList.Contains(u.Id)
                    })
                    .ToListAsync();

                return Json(userList);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ UserListInGroup failed | GroupId={GroupId}", id);
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        [AdminOnly]
        public async Task<IActionResult> CreateUserGroupMapping([FromForm] string userId, [FromForm] int GroupId)
        {
            try
            {
                if (string.IsNullOrEmpty(userId) || GroupId <= 0)
                    return BadRequest(new { success = false, message = "Group And User is required." });

                var currentUserId = GetUserId();

                var userGroupMapping = await _context.GroupUserMapping
                    .FirstOrDefaultAsync(x => x.GroupId == GroupId && x.UserId == userId);

                if (userGroupMapping != null)
                {
                    userGroupMapping.Active = true;
                    userGroupMapping.AddedBy = currentUserId;
                    userGroupMapping.CreatedOn = DateTime.Now;

                    await _context.SaveChangesAsync();

                    return Ok(new { success = true, message = "User added in group successfully!" });
                }

                var groupUserMapping = new GroupUserMapping
                {
                    UserId = userId,
                    GroupId = GroupId,
                    Active = true,
                    AddedBy = currentUserId,
                    RemovedBy = "0",
                    CreatedOn = DateTime.Now
                };

                _context.GroupUserMapping.Add(groupUserMapping);
                await _context.SaveChangesAsync();

                return Ok(new { success = true, message = "User added in group successfully!" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ CreateUserGroupMapping failed | UserId={UserId} | GroupId={GroupId}", userId, GroupId);
                return StatusCode(500, new { success = false, message = $"Error User Mapping with group: {ex.Message}" });
            }
        }

        [HttpPost]
        [AdminOnly]
        public async Task<IActionResult> RemoveUserGroupMapping([FromForm] string userId, [FromForm] int GroupId)
        {
            try
            {
                if (string.IsNullOrEmpty(userId) || GroupId <= 0)
                    return BadRequest(new { success = false, message = "Group And User is required." });

                var currentUserId = GetUserId();

                var userGroupMapping = await _context.GroupUserMapping
                    .FirstOrDefaultAsync(x => x.GroupId == GroupId && x.UserId == userId);

                if (userGroupMapping != null)
                {
                    userGroupMapping.Active = false;
                    userGroupMapping.RemovedBy = currentUserId;

                    await _context.SaveChangesAsync();

                    return Ok(new { success = true, message = "User removed from group successfully!" });
                }

                return BadRequest(new { success = false, message = "Something is wrong, please try again!" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ RemoveUserGroupMapping failed | UserId={UserId} | GroupId={GroupId}", userId, GroupId);
                return StatusCode(500, new { success = false, message = $"Error User Mapping with group: {ex.Message}" });
            }
        }

        #endregion

        private string GetUserId()
        {
            return HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        }

        // ⚠️ NOTE: This method can block threads if called anywhere
        // Keep only for testing, DO NOT use in production
        public async Task<string> DelayResponse()
        {
            await Task.Delay(TimeSpan.FromMinutes(1));
            return "This response was intentionally delayed for testing.";
        }
    }
}
