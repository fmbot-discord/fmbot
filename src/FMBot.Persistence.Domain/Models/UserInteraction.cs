using System;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;

namespace FMBot.Persistence.Domain.Models;

public class UserInteraction
{
    public DateTime Timestamp { get; set; }

    public int UserId { get; set; }

    public ulong? DiscordGuildId { get; set; }
    public ulong? DiscordChannelId { get; set; }

    public UserInteractionType Type { get; set; }

    public string CommandName { get; set; }
    public string CommandContent { get; set; }
    public string[] CommandOptions { get; set; }

    public CommandResponse Response { get; set; }
    public string ErrorReferenceId { get; set; }
    public string ErrorContent { get; set; }
}
