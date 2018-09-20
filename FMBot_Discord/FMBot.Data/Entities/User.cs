using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FMBot.Data.Entities
{
    public class User
    {
        [Key]
        public int UserID { get; set; }

        public int DiscordUserID { get; set; }

        public virtual UserSetting UserSettings { get; set; }

        public ICollection<Guild> Guilds { get; set; }
    }
}
