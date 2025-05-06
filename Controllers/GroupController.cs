using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using SignalRMVC.Areas.Identity.Data;
using SignalRMVC.Models;

namespace SignalRMVC.Controllers
{
    public class GroupController : Controller
    {
        private readonly AppDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public GroupController(AppDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        #region Group
        // GET: Group
        public async Task<IActionResult> Index()
        {
            return View(await _context.ChatRoom.Where(x => !x.isDelete).ToListAsync());
        }

        [HttpGet]
        public async Task<JsonResult> Details(int id)
        {
            var chatRoom = await _context.ChatRoom.FindAsync(id);
            if (chatRoom == null)
            {
                return Json(new { success = false, message = "Chat room not found." });
            }

            return Json(new
            {
                success = true,
                Name = chatRoom.Name
                // Add other fields if needed
            });
        }

        [HttpPost]
        public async Task<JsonResult> EditInline([FromBody] ChatRoom model)
        {
            try
            {
                var chatRoom = await _context.ChatRoom.FindAsync(model.Id);
                if (chatRoom == null)
                {
                    return Json(new { success = false, message = "Chat room not found." });
                }

                var chatsByRoomName = _context.ChatMessages
                    .Where(x => !string.IsNullOrEmpty(x.GroupName))
                    .Where(x => x.GroupName == chatRoom.Name)
                    .ToList(); if (chatsByRoomName != null && chatsByRoomName.Any())
                {
                    chatsByRoomName.ForEach(x => x.GroupName = model.Name);
                    await _context.SaveChangesAsync();
                }
                // Update only the fields that should be editable
                chatRoom.Name = model.Name;
                _context.Update(chatRoom);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Changes saved successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error saving changes: " + ex.Message });
            }
        }

        public async Task<IActionResult> CreateGroup([FromBody] ChatRoom model)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(model.Name))
                    return BadRequest(new { success = false, message = "Group name is required." });

                var group = new ChatRoom
                {
                    Name = model.Name,
                    isDelete = false,
                    CreatedOn = DateTime.Now
                };

                _context.ChatRoom.Add(group);
                await _context.SaveChangesAsync();
                var user = GetUserId();

                var groupUserMapping = new GroupUserMapping
                {
                    UserId = user,
                    GroupId = group.Id,
                    Active = true,
                    AddedBy = user,
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
                return StatusCode(500, new
                {
                    success = false,
                    message = $"Error creating group: {ex.Message}"
                });
            }
        }

        [HttpPost]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var chatRoom = await _context.ChatRoom.FindAsync(id);
                if (chatRoom == null)
                {
                    return Json(new { success = false, message = "Chat room not found." });
                }
                var chatsByRoomName = _context.ChatMessages
                   .Where(x => !string.IsNullOrEmpty(x.GroupName))
                   .Where(x => x.GroupName == chatRoom.Name)
                   .ToList(); if (chatsByRoomName != null && chatsByRoomName.Any())
                {
                    chatsByRoomName.ForEach(x => x.IsDelete = true);
                    await _context.SaveChangesAsync();
                }
                chatRoom.isDelete = true;
                _context.ChatRoom.Update(chatRoom);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Chat room deleted successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error deleting chat room: " + ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> UserChatHistory(string id)
        {
            var chatList = new List<ChatHistory>();
            try
            {
                var user = _context.Users.FirstOrDefault(x => x.Id == id);
                if (user != null)
                {
                    chatList = await _context.ChatMessages.Where(x => x.SenderId == id).OrderByDescending(x=>x.CreatedOn)
                    .Select(u => new ChatHistory
                    {
                        Id = u.Id,
                        Chat = u.Message,
                        GroupName = u.GroupName,
                        Date = u.CreatedOn.Value.ToString("dd-MM-yyyy")
                    }).ToListAsync();

                }

                return Json(chatList);
            }
            catch (Exception es)
            {
                return Json(chatList);
            }
        }

        #endregion

        #region User
        public async Task<IActionResult> UserList()
        {
            var user = GetUserId();
            var userListabc = _userManager.Users;

            var userRoles = from role in _context.Roles
                            join userRole in _context.UserRoles on role.Id equals userRole.RoleId
                            select new
                            {
                                Role = role,
                                UserId = userRole.UserId,
                                RoleName = role.Name
                            };

            var userList = await _userManager.Users.Where(x => !x.IsDeleted)
                .Select(u => new UserViewModel
                {
                    Id = u.Id,
                    UserName = u.FullName,
                    Email = u.Email,
                    LoginName = u.UserName,
                    RoleName = userRoles != null && userRoles.Any() ? userRoles.FirstOrDefault(x => x.UserId == u.Id).RoleName : "",
                    IsCurrentUser = (user == u.Id ? true : false),
                    PhoneNumber = u.PhoneNumber,
                    LockoutEnd = u.LockoutEnd,
                    LockoutEnabled = u.LockoutEnabled,
                    AccessFailedCount = u.AccessFailedCount
                })
                .ToListAsync();
            return View(userList);
        }

        [HttpPost]
        public async Task<IActionResult> LockUser(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return Json(new { success = false, message = "User not found." });
            }

            var result = await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.Now.AddYears(10));

            return Json(new
            {
                success = result.Succeeded,
                message = result.Succeeded ? "User locked" : string.Join(", ", result.Errors.Select(e => e.Description))
            });
        }

        [HttpPost]
        public async Task<IActionResult> UnlockUser(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return Json(new { success = false, message = "User not found." });
            }

            var result = await _userManager.SetLockoutEndDateAsync(user, null);

            return Json(new
            {
                success = result.Succeeded,
                message = result.Succeeded ? "User unlocked" : string.Join(", ", result.Errors.Select(e => e.Description))
            });
        }

        [HttpPost]
        public async Task<IActionResult> DeleteUser(string id)
        {
            var user = await _userManager.FindByIdAsync(id);
            if (user == null)
            {
                return Json(new { success = false, message = "User not found." });
            }
            user.IsDeleted = true;
            _context.Update(user);
            _context.SaveChanges();
            return Json(new
            {
                success = true,
                message = "User deleted"
            });
        }

        private string GetUserId()
        {
            var userId = HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return userId;
        }

        public async Task<IActionResult> AddUser()
        {
            var user = new UserModal();

            var role = _context.Roles.ToList();
            foreach (var item in role)
            {
                user.UserRoles.Add(new SelectListItem { Text = item.Name, Value = item.Id });
            }

            return View(user);
        }

        [HttpPost]
        public async Task<IActionResult> AddUser(UserModal model)
        {
            ModelState.Remove("Id");

            if (!ModelState.IsValid)
            {
                // Repopulate UserRoles if validation fails
                model.UserRoles = await GetUserRoles(); // Implement this method to fetch roles
                return View(model);
            }

            // Check if email already exists
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
                ModelState.AddModelError("NormalizedUserName", "User Name is already Exists");
                model.UserRoles = await GetUserRoles();
                return View(model);
            }

            // Create new ApplicationUser
            var user = new ApplicationUser
            {
                FullName = model.UserName,
                UserName = model.NormalizedUserName,
                NormalizedUserName = model.Email,
                Email = model.Email,
                EmailConfirmed = true, // Set to true if you're not implementing email confirmation
                NormalizedEmail = model.Email,
                PhoneNumber = "",
                TwoFactorEnabled = false,
                LockoutEnabled = true,
                IsDeleted = false
            };

            // Create the user with password
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

            // Assign selected role to the user
            if (!string.IsNullOrEmpty(model.RoleId))
            {
                var role = _context.Roles.FirstOrDefault(x => x.Id == model.RoleId);
                if (role != null)
                {
                    var roleResult = await _userManager.AddToRoleAsync(user, role.Name);
                    if (!roleResult.Succeeded)
                    {
                        // Log role assignment errors if needed
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

        // GET: EditUser/{id}
        [HttpGet]
        public async Task<IActionResult> EditUser(string id)
        {
            var user = _context.Users.FirstOrDefault(u => u.Id == id);
            if (user == null)
            {
                return NotFound();
            }

            var userRole = _context.UserRoles.FirstOrDefault(x => x.UserId == user.Id);
            var model = new UserModal();
            model.Id = user.Id;
            model.UserName = user.FullName;
            model.PhoneNumber = user.PhoneNumber;
            model.Email = user.Email;
            model.RoleId = userRole != null ? userRole.RoleId : "";
            model.UserRoles.AddRange(GetUserRoles().Result);

            return View(model);
        }

        // POST: EditUser
        [HttpPost]
        public IActionResult EditUser(UserModal model)
        {
            if (ModelState.IsValid)
            {
                var user = _context.Users.FirstOrDefault(u => u.Id == model.Id);
                if (user == null)
                {
                    return NotFound();
                }

                user.FullName = model.UserName;
                user.PhoneNumber = model.PhoneNumber;
                user.PasswordHash = model.PasswordHash;

                if (!string.IsNullOrEmpty(model.PasswordHash))
                {
                    // Update password if provided
                    if (!string.IsNullOrEmpty(model.PasswordHash))
                    {

                        var token = _userManager.GeneratePasswordResetTokenAsync(user).Result;
                        var passwordResult = _userManager.ResetPasswordAsync(user, token, user.PasswordHash).Result;
                        if (!passwordResult.Succeeded)
                        {
                            foreach (var error in passwordResult.Errors)
                            {
                                ModelState.AddModelError("", error.Description);
                            }
                            model.UserRoles = GetUserRoles().Result;
                            return View(model);
                        }
                    }
                }
                var userRole = _context.UserRoles.FirstOrDefault(x => x.UserId == user.Id && x.RoleId != model.RoleId);
                if (userRole != null)
                {
                    _context.UserRoles.Remove(userRole);
                    var role = _context.Roles.FirstOrDefault(x => x.Id == model.RoleId);
                    if (role != null)
                    {
                        var roleResult = _userManager.AddToRoleAsync(user, role.Name).Result;
                    }
                }
                else
                {
                    var role = _context.Roles.FirstOrDefault(x => x.Id == model.RoleId);
                    if (role != null)
                    {
                        var roleResult = _userManager.AddToRoleAsync(user, role.Name).Result;
                    }
                }
                _context.SaveChanges();
                return RedirectToAction("UserList");
            }

            model.UserRoles = GetUserRoles().Result;
            return View(model);
        }

        // Helper method to get roles for dropdown
        private async Task<List<SelectListItem>> GetUserRoles()
        {
            var roles = await _context.Roles.ToListAsync();
            return roles.Select(r => new SelectListItem
            {
                Value = r.Id,
                Text = r.Name
            }).ToList();
        }

        #endregion

        #region Group User Mapping

        [HttpGet]
        public async Task<IActionResult> UserListInGroup(int id)
        {
            try
            {
                var user = GetUserId();
                var groupUserList = await _context.GroupUserMapping.Where(x => x.Active && x.GroupId == id).Select(x => x.UserId).ToListAsync();

                var userRoles = from role in _context.Roles
                                join userRole in _context.UserRoles on role.Id equals userRole.RoleId
                                select new
                                {
                                    Role = role,
                                    UserId = userRole.UserId,
                                    RoleName = role.Name
                                };

                var userList = await _context.Users
                    .Select(u => new UserViewModel
                    {
                        Id = u.Id,
                        UserName = u.FullName,
                        Email = u.Email,
                        RoleName = userRoles != null && userRoles.Any() ? userRoles.FirstOrDefault(x => x.UserId == u.Id).RoleName : "",
                        IsCurrentUser = (user == u.Id ? true : false),
                        PhoneNumber = u.PhoneNumber,
                        IsAlreadyInGroup = (groupUserList.Contains(u.Id) ? true : false)
                    }).ToListAsync();

                return Json(userList);
            }
            catch (Exception es)
            {
                return Json(es);
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateUserGroupMapping([FromForm] string userId, [FromForm] int GroupId)
        {
            try
            {
                if (string.IsNullOrEmpty(userId) || GroupId <= 0)
                    return BadRequest(new { success = false, message = "Group And User is required." });
                var user = GetUserId();

                var userGroupMapping = _context.GroupUserMapping.FirstOrDefault(x => x.GroupId == GroupId && x.UserId == userId);
                if (userGroupMapping != null)
                {
                    userGroupMapping.Active = true;
                    userGroupMapping.AddedBy = user;
                    userGroupMapping.CreatedOn = DateTime.Now;
                    _context.GroupUserMapping.Update(userGroupMapping);
                    await _context.SaveChangesAsync();
                    return Ok(new
                    {
                        success = true,
                        message = "User Add in Group successfully!",
                    });
                }

                var groupUserMapping = new GroupUserMapping
                {
                    UserId = userId,
                    GroupId = GroupId,
                    Active = true,
                    AddedBy = user,
                    RemovedBy = "0",
                    CreatedOn = DateTime.Now
                };

                _context.GroupUserMapping.Add(groupUserMapping);
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "User Add in Group successfully!",
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = $"Error User Mapping with group: {ex.Message}"
                });
            }
        }

        [HttpPost]
        public async Task<IActionResult> RemoveUserGroupMapping([FromForm] string userId, [FromForm] int GroupId)
        {
            try
            {
                if (string.IsNullOrEmpty(userId) || GroupId <= 0)
                    return BadRequest(new { success = false, message = "Group And User is required." });
                var user = GetUserId();
                var userGroupMapping = _context.GroupUserMapping.FirstOrDefault(x => x.GroupId == GroupId && x.UserId == userId);
                if (userGroupMapping != null)
                {
                    userGroupMapping.Active = false;
                    userGroupMapping.RemovedBy = user;
                    _context.GroupUserMapping.Update(userGroupMapping);
                    await _context.SaveChangesAsync();
                    return Ok(new
                    {
                        success = true,
                        message = "User Add in Group successfully!",
                    });
                }
                return BadRequest(new
                {
                    success = false,
                    message = "Some thing is wrong Please try again!",
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = $"Error User Mapping with group: {ex.Message}"
                });
            }
        }

        #endregion

    }
}
