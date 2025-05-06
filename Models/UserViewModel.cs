namespace SignalRMVC.Models
{
    public class UserViewModel
    {
        public string Id { get; set; }
        public string UserName { get; set; }
        public string LoginName { get; set; }
        public string Email { get; set; }
        public bool IsCurrentUser { get; set; }
        public bool IsAlreadyInGroup { get; set; }
        public string PhoneNumber { get; set; }
        public bool TwoFactorEnabled { get; set; }
        public DateTimeOffset? LockoutEnd { get; set; }
        public bool LockoutEnabled { get; set; }
        public int AccessFailedCount { get; set; }
        public string RoleName { get; set; }
    }

    public class ChatHistory
    {
        public int Id { get; set; }
        public string GroupName { get; set; }
        public string Chat { get; set; }
        public string Date { get; set; }
    }

}
