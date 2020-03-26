using System;
using System.Collections.Generic;

namespace FMBot.Data.Entities
{
    public partial class User
    {
        public User()
        {
            FriendsFriendUser = new HashSet<Friend>();
            FriendsUser = new HashSet<Friend>();
        }

        public int UserID { get; set; }
        public ulong DiscordUserID { get; set; }
        public bool? Featured { get; set; }
        public bool? Blacklisted { get; set; }
        public UserType UserType { get; set; }
        public bool? TitlesEnabled { get; set; }
        public string UserNameLastFM { get; set; }
        public ChartType ChartType { get; set; }
        public ChartTimePeriod ChartTimePeriod { get; set; }
        public DateTime? LastGeneratedChartDateTimeUtc { get; set; }

        public ICollection<Friend> FriendsFriendUser { get; set; }
        public ICollection<Friend> FriendsUser { get; set; }
    }
}
