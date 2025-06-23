namespace SignalRMVC.Models
{
    public class UsersMessage
    {
        public int Id { get; set; }
        public string? SenderId { get; set; } = string.Empty;
        public string? ReceiverId { get; set; } = string.Empty;
        public string? Message { get; set; } = string.Empty;
        public bool IsDelete { get; set; }
        public DateTime? CreatedOn { get; set; }
    }
}
