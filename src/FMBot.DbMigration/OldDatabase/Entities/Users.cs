using System;
using System.Collections.Generic;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;

namespace FMBot.DbMigration.OldDatabase.Entities
{
    public partial class User
    {
        public User()
        {
            FriendsFriendUser = new HashSet<Friend>();
            FriendsUser = new HashSet<Friend>();
            GuildUsers = new HashSet<GuildUsers>();
        }

        public int UserID { get; set; }
        public string DiscordUserID { get; set; }
        public bool? Featured { get; set; }
        public bool? Blacklisted { get; set; }
        public UserType UserType { get; set; }
        public bool? TitlesEnabled { get; set; }
        public string UserNameLastFM { get; set; }
        public FmEmbedType FmEmbedType { get; set; }
        public TimePeriod ChartTimePeriod { get; set; }
        public DateTime? LastGeneratedChartDateTimeUtc { get; set; }

        public ICollection<Friend> FriendsFriendUser { get; set; }
        public ICollection<Friend> FriendsUser { get; set; }
        public ICollection<GuildUsers> GuildUsers { get; set; }
    }
}
