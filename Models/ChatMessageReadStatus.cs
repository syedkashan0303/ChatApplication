namespace SignalRMVC.Models
{
    public class ChatMessageReadStatus
    {
        public int Id { get; set; }

        public int ChatMessageId { get; set; }
        public ChatMessage ChatMessage { get; set; }

        public string UserId { get; set; }
        public ApplicationUser User { get; set; }

        public bool IsRead { get; set; } = false;
        public DateTime? ReadOn { get; set; }
    }

}
