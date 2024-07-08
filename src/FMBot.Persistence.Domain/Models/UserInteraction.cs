using System;
using System.Collections.Generic;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;

namespace FMBot.Persistence.Domain.Models;

public class UserInteraction
{
    public long Id { get; set; }

    public DateTime Timestamp { get; set; }

    public int UserId { get; set; }

    public ulong? DiscordGuildId { get; set; }
    public ulong? DiscordChannelId { get; set; }
    public ulong? DiscordId { get; set; }
    public ulong? DiscordResponseId { get; set; }

    public UserInteractionType Type { get; set; }

    public string CommandName { get; set; }
    public string CommandContent { get; set; }
    public Dictionary<string, string> CommandOptions { get; set; }

    public CommandResponse Response { get; set; }

    public string ErrorReferenceId { get; set; }
    public string ErrorContent { get; set; }

    public string Artist { get; set; }
    public string Album { get; set; }
    public string Track { get; set; }

    public bool? HintShown { get; set; }

    public User User { get; set; }
}
