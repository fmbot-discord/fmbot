using FMBot.Domain.Models;

namespace FMBot.Persistence.Domain.Models;

public class GuildShortcut : Shortcut
{
    public int GuildId { get; set; }
    public Guild Guild { get; set; }
}
