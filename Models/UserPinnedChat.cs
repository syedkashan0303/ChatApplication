namespace SignalRMVC.Models
{
    // A chat a user has pinned to the top of their own chat list.
    // TargetId is the ChatRoom.Id (as string) when IsRoom, otherwise the other user's Id.
    public class UserPinnedChat
    {
        public int Id { get; set; }

        public string UserId { get; set; } = string.Empty;
        public ApplicationUser User { get; set; } = null!;

        public bool IsRoom { get; set; }
        public string TargetId { get; set; } = string.Empty;

        public DateTime CreatedOn { get; set; }
    }
}
