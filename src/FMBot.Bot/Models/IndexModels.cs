using System;
using FMBot.Domain.Models;

namespace FMBot.Bot.Models;

public class IndexedUserUpdateDto
{
    public string UserName { get; set; }

    public int? GuildId { get; set; }

    public int UserId { get; set; }

    public bool? WhoKnowsWhitelisted { get; set; }
}
