using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FMBot.Data.Entities
{
    public class User
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int UserID { get; set; }


        public string DiscordUserID { get; set; }


        public bool? Featured { get; set; }

        public bool? Blacklisted { get; set; }

        public bool? TitlesEnabled { get; set; }


        public string UserNameLastFM { get; set; }


        public ChartType ChartType { get; set; }

        public ChartTimePeriod ChartTimePeriod { get; set; }

        public UserType UserType { get; set; }


        public ICollection<Guild> Guilds { get; set; }
    }
}
