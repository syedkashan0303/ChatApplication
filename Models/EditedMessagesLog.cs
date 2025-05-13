namespace SignalRMVC.Models
{
    public class EditedMessagesLog
    {
        public int Id { get; set; }
        public int MessageId { get; set; }
        public string GroupName { get; set; }
        public string Message { get; set; }
        public string EditedBy { get; set; }
        public DateTime EditedOn { get; set; }
    }
}
