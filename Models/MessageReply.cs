using SignalRMVC.Areas.Identity.Data;

namespace SignalRMVC.Models
{
    public class MessageReply
    {
        public long Id { get; set; }
        public int ParentMessageId { get; set; }
        public ChatMessage? ParentMessage { get; set; }
        public string? ReplyText { get; set; }
        public string? UserId { get; set; }
        public ApplicationUser? User { get; set; }
        public DateTime CreatedOn { get; set; }
        public DateTime? UpdatedOn { get; set; }
        public bool IsDeleted { get; set; }
    }
}
