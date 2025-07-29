namespace SignalRMVC.Models
{
    public class UsersMessageReadStatus
    {

        public int Id { get; set; }

        public int ChatMessageId { get; set; }
        public UsersMessage ChatMessage { get; set; }

        public string SenderId { get; set; }
        public ApplicationUser Sender { get; set; }

        public string ReceiverId { get; set; }
        public ApplicationUser Receiver { get; set; }

        public bool IsRead { get; set; } = false;
        public DateTime CreatedOn { get; set; }

    }
}
