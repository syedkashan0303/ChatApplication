namespace SignalRMVC.Models
{
    public class ChatMessage
    {
        public int Id { get; set; }
        public string? SenderId { get; set; } = string.Empty;
        public string? ReceiverId { get; set; } = string.Empty;// Can be null for broadcast/group
        public string? Message { get; set; } = string.Empty;
        public string? GroupName { get; set; } = string.Empty;
        public bool IsDelete { get; set; }
        public DateTime? CreatedOn { get; set; }
    }

}
