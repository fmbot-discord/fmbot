using System;
using System.Collections.Generic;

namespace FMBot.Persistence.Domain.Models
{
    public class User
    {
        public int UserId { get; set; }

        public ulong DiscordUserId { get; set; }

        public bool? Featured { get; set; }

        public bool? FeaturedNotificationsEnabled { get; set; }

        public bool? Blacklisted { get; set; }

        public UserType UserType { get; set; }

        public bool? TitlesEnabled { get; set; }

        public string UserNameLastFM { get; set; }

        public FmEmbedType FmEmbedType { get; set; }

        public FmCountType? FmCountType { get; set; }

        public ChartTimePeriod ChartTimePeriod { get; set; }

        public DateTime? LastGeneratedChartDateTimeUtc { get; set; }

        public DateTime? LastIndexed { get; set; }

        public ICollection<Friend> FriendedByUsers { get; set; }

        public ICollection<Friend> Friends { get; set; }

        public ICollection<Artist> Artists { get; set; }

        public ICollection<GuildUser> Guilds { get; set; }
    }
}
