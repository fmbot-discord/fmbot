using System;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;

namespace FMBot.Persistence.Domain.Models;

public class GuildAutopostRun
{
    public long Id { get; set; }

    public int AutopostId { get; set; }

    public int GuildId { get; set; }

    public AutopostType ContentType { get; set; }

    public DateTime PeriodStart { get; set; }

    public DateTime PeriodEnd { get; set; }

    public DateTime PostedAt { get; set; }

    public ulong? MessageId { get; set; }

    public AutopostSnapshot Snapshot { get; set; }

    public GuildAutopost Autopost { get; set; }
}
