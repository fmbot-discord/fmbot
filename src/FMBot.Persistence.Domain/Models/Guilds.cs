using System;
using System.Collections.Generic;

namespace FMBot.Persistence.Domain.Models
{
    public class Guild
    {
        public int GuildId { get; set; }

        public ulong DiscordGuildId { get; set; }

        public string Name { get; set; }

        public string Prefix { get; set; }

        public bool? Blacklisted { get; set; }

        public bool? TitlesEnabled { get; set; }

        public FmEmbedType FmEmbedType { get; set; }

        public ChartTimePeriod ChartTimePeriod { get; set; }

        public string[] EmoteReactions { get; set; }

        public string[] DisabledCommands { get; set; }

        public DateTime? LastIndexed { get; set; }

        public bool? SpecialGuild { get; set; }

        public ICollection<GuildUser> GuildUsers { get; set; }
    }
}
