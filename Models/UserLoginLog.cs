namespace SignalRMVC.Models
{
    public class UserLoginLog
    {
        public long Id { get; set; }
        public string UserId { get; set; }
        public DateTime LoginDateTime { get; set; } = DateTime.UtcNow;
    }
}
