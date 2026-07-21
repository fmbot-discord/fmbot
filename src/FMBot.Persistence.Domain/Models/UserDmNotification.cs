using System;
using FMBot.Domain.Enums;

namespace FMBot.Persistence.Domain.Models;

public class UserDmNotification
{
    public long Id { get; set; }

    public int UserId { get; set; }

    public ulong DiscordUserId { get; set; }

    public UserDmNotificationType Type { get; set; }

    public DateTime Sent { get; set; }

    public string Reference { get; set; }

    public bool Successful { get; set; }

    public User User { get; set; }
}
