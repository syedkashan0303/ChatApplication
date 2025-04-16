using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
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
        private readonly UserManager<IdentityUser> _userManager;


        public GroupController(AppDbContext context, UserManager<IdentityUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

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

        [HttpPost]
        public async Task<IActionResult> CreateRoom([FromBody] string roomName)
        {
            if (!string.IsNullOrWhiteSpace(roomName))
            {
                var exists = await _context.ChatRoom.AnyAsync(r => r.Name == roomName);
                if (!exists)
                {
                    _context.ChatRoom.Add(new ChatRoom
                    {
                        Name = roomName,
                        isDelete = false,
                        CreatedOn = DateTime.Now
                    });
                    await _context.SaveChangesAsync();
                }
            }
            return Ok();
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

        #region
        public async Task<IActionResult> UserList()
        {

            var user = GetUserId();
            var userList = await _context.Users
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
        public async Task<IActionResult> DeleteUser(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null)
            {
                return Json(new { success = false, message = "User not found." });
            }

            var result = await _userManager.DeleteAsync(user);

            return Json(new
            {
                success = result.Succeeded,
                message = result.Succeeded ? "User deleted" : string.Join(", ", result.Errors.Select(e => e.Description))
            });
        }

        private string GetUserId()
        {
            var userId = HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return userId;
        }

        #endregion

    }
}
