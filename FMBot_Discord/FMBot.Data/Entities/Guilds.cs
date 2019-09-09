using System;
using System.Collections.Generic;

namespace FMBot.Data.Entities
{
    public partial class Guild
    {
        public Guild()
        {
            GuildUsers = new HashSet<GuildUsers>();
        }

        public int GuildID { get; set; }
        public string DiscordGuildID { get; set; }
        public string Name { get; set; }
        public bool? Blacklisted { get; set; }
        public bool? TitlesEnabled { get; set; }
        public ChartType ChartType { get; set; }
        public ChartTimePeriod ChartTimePeriod { get; set; }

        public virtual ICollection<GuildUsers> GuildUsers { get; set; }
    }
}
