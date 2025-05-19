namespace SignalRMVC.Models
{
    public class ChatLog
    {
        public int Id { get; set; }

        public string UserId { get; set; }

        public string ActionName { get; set; }

        public string Details { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
    }

}
