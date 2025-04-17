namespace SignalRMVC.Models
{
    public class GroupUserMapping
    {
        public int Id { get; set; }
        public string UserId { get; set; }
        public int GroupId { get; set; }
        public bool Active { get; set; }
        public string AddedBy { get; set; }
        public string RemovedBy { get; set; }
        public DateTime CreatedOn { get; set; }
    }
}
