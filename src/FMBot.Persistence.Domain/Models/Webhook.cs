using System;
using Shared.Domain.Enums;

namespace FMBot.Persistence.Domain.Models;

public class Webhook
{
    public int Id { get; set; }

    public ulong DiscordWebhookId { get; set; }

    public ulong? DiscordThreadId { get; set; }

    public int GuildId { get; set; }

    public string Token { get; set; }

    public BotType BotType { get; set; }

    public DateTime Created { get; set; }

    public Guild Guild { get; set; }
}
