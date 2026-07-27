using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SignalRMVC.Models;

namespace SignalRMVC.Controllers
{
    [Authorize]
    public class AccountController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;
        private readonly ILogger<AccountController> _logger;

        public AccountController(
            UserManager<ApplicationUser> userManager,
            SignInManager<ApplicationUser> signInManager,
            ILogger<AccountController> logger)
        {
            _userManager = userManager;
            _signInManager = signInManager;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetUserProfile()
        {
            try
            {
                var user = await _userManager.GetUserAsync(User);
                if (user == null)
                {
                    return Json(new { success = false, message = "User session expired or not found." });
                }

                return Json(new
                {
                    success = true,
                    fullName = user.FullName ?? string.Empty,
                    email = user.Email ?? string.Empty,
                    userName = user.UserName ?? string.Empty,
                    phoneNumber = user.PhoneNumber ?? string.Empty
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching user profile.");
                return Json(new { success = false, message = "An error occurred while retrieving user details." });
            }
        }

        [HttpPost]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest model)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    var errors = ModelState
                        .Where(x => x.Value?.Errors.Count > 0)
                        .ToDictionary(
                            kvp => kvp.Key.Replace("model.", "").Replace("Model.", ""),
                            kvp => kvp.Value?.Errors.Select(e => e.ErrorMessage).FirstOrDefault() ?? "Invalid value."
                        );
                    return Json(new { success = false, errors });
                }

                var user = await _userManager.GetUserAsync(User);
                if (user == null)
                {
                    return Json(new { success = false, message = "User session expired or not found." });
                }

                user.FullName = model.FullName?.Trim();
                user.PhoneNumber = model.PhoneNumber?.Trim();

                var result = await _userManager.UpdateAsync(user);
                if (!result.Succeeded)
                {
                    var errors = new Dictionary<string, string>();
                    foreach (var err in result.Errors)
                    {
                        if (err.Description.Contains("Phone", StringComparison.OrdinalIgnoreCase))
                        {
                            errors["PhoneNumber"] = err.Description;
                        }
                        else if (err.Description.Contains("Name", StringComparison.OrdinalIgnoreCase))
                        {
                            errors["FullName"] = err.Description;
                        }
                        else
                        {
                            errors["General"] = err.Description;
                        }
                    }
                    return Json(new { success = false, errors });
                }

                return Json(new { success = true, message = "Profile updated successfully!", fullName = user.FullName });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating profile.");
                return Json(new { success = false, message = "An unexpected error occurred while updating profile." });
            }
        }

        [HttpPost]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest model)
        {
            try
            {
                if (!ModelState.IsValid)
                {
                    var errors = ModelState
                        .Where(x => x.Value?.Errors.Count > 0)
                        .ToDictionary(
                            kvp => kvp.Key.Replace("model.", "").Replace("Model.", ""),
                            kvp => kvp.Value?.Errors.Select(e => e.ErrorMessage).FirstOrDefault() ?? "Invalid value."
                        );
                    return Json(new { success = false, errors });
                }

                var user = await _userManager.GetUserAsync(User);
                if (user == null)
                {
                    return Json(new { success = false, message = "User session expired or not found." });
                }

                var token = await _userManager.GeneratePasswordResetTokenAsync(user);
                var changeResult = await _userManager.ResetPasswordAsync(user, token, model.NewPassword);
                if (!changeResult.Succeeded)
                {
                    var errors = new Dictionary<string, string>();
                    foreach (var error in changeResult.Errors)
                    {
                        errors["NewPassword"] = error.Description;
                    }
                    return Json(new { success = false, errors });
                }

                await _signInManager.RefreshSignInAsync(user);
                return Json(new { success = true, message = "Password changed successfully!" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error changing password.");
                return Json(new { success = false, message = "An unexpected error occurred while changing password." });
            }
        }
    }
}
