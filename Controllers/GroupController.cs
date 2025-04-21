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
        #endregion

        #region User
        public async Task<IActionResult> UserList()
        {
            var user = GetUserId();
            var userListabc = _userManager.Users;


            var userList = await _userManager.Users.Where(x => !x.IsDeleted)
                .Select(u => new UserViewModel
                {
                    Id = u.Id,
                    UserName = u.UserName,
                    Email = u.Email,
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

        #endregion

        #region Group User Mapping

        [HttpGet]
        public async Task<IActionResult> UserListInGroup(int id)
        {
            try
            {
                var user = GetUserId();
                var groupUserList = await _context.GroupUserMapping.Where(x => x.Active && x.GroupId == id).Select(x => x.UserId).ToListAsync();
                var userList = await _context.Users
                    .Select(u => new UserViewModel
                    {
                        Id = u.Id,
                        UserName = u.UserName,
                        Email = u.Email,
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
