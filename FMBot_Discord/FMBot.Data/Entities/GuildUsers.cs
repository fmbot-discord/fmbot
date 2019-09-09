using System;
using System.Collections.Generic;

namespace FMBot.Data.Entities
{
    public partial class GuildUsers
    {
        public int GuildID { get; set; }
        public int UserID { get; set; }

        public virtual Guild Guild { get; set; }
        public virtual User User { get; set; }
    }
}
