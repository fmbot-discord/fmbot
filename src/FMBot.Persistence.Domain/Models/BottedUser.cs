using System;

namespace FMBot.Persistence.Domain.Models
{
    public class BottedUser
    {
        public int BottedUserId { get; set; }

        public string UserNameLastFM { get; set; }

        public DateTime? LastFmRegistered { get; set; }

        public string Notes { get; set; }
    }
}
