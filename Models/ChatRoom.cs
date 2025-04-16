namespace SignalRMVC.Models
{
    public class ChatRoom
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public bool isDelete { get; set; }
        public DateTime? CreatedOn { get; set; }
    }
}
