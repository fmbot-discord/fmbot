
namespace FMBot.Persistence.Domain.Models;

public class Friend
{
    public int FriendId { get; set; }

    public int UserId { get; set; }

    public string LastFMUserName { get; set; }

    public int? FriendUserId { get; set; }

    public User FriendUser { get; set; }

    public User User { get; set; }
}
