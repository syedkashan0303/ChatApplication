using Microsoft.AspNetCore.Identity;
using SignalRMVC.Models;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using System.Threading.Tasks;

namespace SignalRMVC.CustomClasses
{
    public class UserInfoService
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public UserInfoService(UserManager<ApplicationUser> userManager, IHttpContextAccessor httpContextAccessor)
        {
            _userManager = userManager;
            _httpContextAccessor = httpContextAccessor;
        }

        public async Task<ApplicationUser> GetCurrentUserAsync()
        {
            var user = _httpContextAccessor.HttpContext?.User;
            return await _userManager.GetUserAsync(user);
        }

        public async Task<string> GetUserNameAsync()
        {
            var user = await GetCurrentUserAsync();
            return user?.UserName;
        }

        public async Task<string> GetFullNameAsync()
        {
            var user = await GetCurrentUserAsync();
            return $"{user?.FullName}";
        }
    }
}
