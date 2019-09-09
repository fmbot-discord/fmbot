using FMBot.Data.Entities;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FMBot.Data.Entities_old
{
    public class Guild
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int GuildID { get; set; }

        public string DiscordGuildID { get; set; }

        public string Name { get; set; }


        public bool? Blacklisted { get; set; }

        public bool? TitlesEnabled { get; set; }


        public ChartType ChartType { get; set; }

        public ChartTimePeriod ChartTimePeriod { get; set; }


        public ICollection<User> Users { get; set; }
    }
}
