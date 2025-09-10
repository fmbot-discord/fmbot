using FMBot.Domain.Models;

namespace FMBot.Persistence.Domain.Models;

public class UserShortcut : Shortcut
{
    public int UserId { get; set; }
    public User User { get; set; }
}
