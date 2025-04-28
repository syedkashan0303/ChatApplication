using Microsoft.AspNetCore.Identity;

namespace SignalRMVC.Models
{
    public class ApplicationUser : IdentityUser
    {
        public string? FullName { get; set; }
        public bool IsDeleted { get; set; }
        public bool IsDarkTheme { get; set; }
    }
}
