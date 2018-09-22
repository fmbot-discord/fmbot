using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FMBot.Data.Entities
{
    public class Guild
    {
        [Key]
        public int GuildID { get; set; }

        public string DiscordGuildID { get; set; }

        public string Name { get; set; }

        public ICollection<User> Users { get; set; }

        public virtual Settings Settings { get; set; }
    }
}
