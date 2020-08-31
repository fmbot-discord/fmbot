using System.Collections.Generic;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;

namespace FMBot.DbMigration.OldDatabase.Entities
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
        public FmEmbedType FmEmbedType { get; set; }
        public ChartTimePeriod ChartTimePeriod { get; set; }

        public string[] EmoteReactions { get; set; }

        public virtual ICollection<GuildUsers> GuildUsers { get; set; }
    }
}
