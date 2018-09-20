using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FMBot.Data.Entities
{
    public class UserSetting
    {
        [Key, ForeignKey("User")]
        public int UserID { get; set; }

        public string UserNameLastFM { get; set; }

        public ChartType ChartType { get; set; }

        public ChartTimePeriod ChartTimePeriod { get; set; }

        public bool TitlesEnabled { get; set; }

        public User User { get; set; }
    }
}
