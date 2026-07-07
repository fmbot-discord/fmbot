using System;
using System.Collections.Generic;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;

namespace FMBot.Persistence.Domain.Models;

public class GuildAutopost
{
    public int Id { get; set; }

    public int GuildId { get; set; }

    public ulong ChannelId { get; set; }

    public AutopostType ContentType { get; set; }

    public AutopostSchedule Schedule { get; set; }

    public ulong[] RoleIds { get; set; }

    public string ArtistFilter { get; set; }

    public AutopostSize ContentSize { get; set; }

    public TimePeriod? TimePeriod { get; set; }

    public bool Enabled { get; set; }

    public DateTime? LastPosted { get; set; }

    public ulong? LastMessageId { get; set; }

    public ulong CreatedBy { get; set; }

    public DateTime Created { get; set; }

    public DateTime Modified { get; set; }

    public Guild Guild { get; set; }

    public ICollection<GuildAutopostRun> Runs { get; set; }
}
