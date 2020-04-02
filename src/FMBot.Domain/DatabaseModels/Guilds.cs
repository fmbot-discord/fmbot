using System;

namespace FMBot.Domain.DatabaseModels
{
    public class Guild
    {
        public int GuildId { get; set; }

        public ulong DiscordGuildId { get; set; }

        public string Name { get; set; }

        public bool? Blacklisted { get; set; }

        public bool? TitlesEnabled { get; set; }

        public ChartType ChartType { get; set; }

        public ChartTimePeriod ChartTimePeriod { get; set; }

        public string[] EmoteReactions { get; set; }

        public DateTime? LastIndexed { get; set; }

        public bool? SpecialGuild { get; set; }
    }
}
