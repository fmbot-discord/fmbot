using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FMBot.Data.Entities
{
    public class Friend
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int FriendID { get; set; }

        [ForeignKey("User")]
        public int UserID { get; set; }

        public string LastFMUserName { get; set; }

        [ForeignKey("FriendUser")]
        public int? FriendUserID { get; set; }

        public virtual User User { get; set; }

        public virtual User FriendUser { get; set; }
    }
}
