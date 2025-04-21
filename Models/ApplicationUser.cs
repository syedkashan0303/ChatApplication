using Microsoft.AspNetCore.Identity;

namespace SignalRMVC.Models
{
    public class ApplicationUser : IdentityUser
    {
        public bool IsDeleted { get; set; }
    }
}
