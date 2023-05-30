namespace FMBot.Bot.Models;

public class IndexedUserUpdateDto
{
    public string UserName { get; set; }

    public int? GuildId { get; set; }

    public int UserId { get; set; }

    public decimal[] Roles { get; set; }
}
