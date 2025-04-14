namespace SignalRMVC.Models
{
    public class ChatMessage
    {
        public int Id { get; set; }
        public string SenderId { get; set; }
        public string ReceiverId { get; set; } // Can be null for broadcast/group
        public string Message { get; set; }
        public string GroupName { get; set; }
        public DateTime CreatedOn { get; set; }
    }

}
