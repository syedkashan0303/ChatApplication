namespace SignalRMVC.Models
{
    // One reaction per user per group message (a new emoji replaces the previous one).
    public class ChatMessageReaction
    {
        public int Id { get; set; }

        public int ChatMessageId { get; set; }
        public ChatMessage ChatMessage { get; set; } = null!;

        public string UserId { get; set; } = string.Empty;
        public ApplicationUser User { get; set; } = null!;

        public string Emoji { get; set; } = string.Empty;
        public DateTime CreatedOn { get; set; }
    }
}
