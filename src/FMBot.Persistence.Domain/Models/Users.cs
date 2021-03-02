using System;
using System.Collections.Generic;
using System.Reflection.Metadata;
using FMBot.Domain.Models;

namespace FMBot.Persistence.Domain.Models
{
    public class User
    {
        public int UserId { get; set; }

        public ulong DiscordUserId { get; set; }

        public bool? Featured { get; set; }

        public bool? FeaturedNotificationsEnabled { get; set; }

        public bool? Blocked { get; set; }

        public UserType UserType { get; set; }

        public bool? TitlesEnabled { get; set; }

        public string UserNameLastFM { get; set; }

        public string SessionKeyLastFm { get; set; }

        public long? TotalPlaycount { get; set; }

        public bool? RymEnabled { get; set; }

        public FmEmbedType FmEmbedType { get; set; }

        public FmCountType? FmCountType { get; set; }

        public ChartTimePeriod ChartTimePeriod { get; set; }

        public PrivacyLevel PrivacyLevel { get; set; }

        public DateTime? LastGeneratedChartDateTimeUtc { get; set; }

        public DateTime? LastIndexed { get; set; }

        public DateTime? LastUpdated { get; set; }

        public DateTime? LastScrobbleUpdate { get; set; }

        public DateTime? LastUsed { get; set; }

        public ICollection<Friend> FriendedByUsers { get; set; }

        public ICollection<Friend> Friends { get; set; }

        public ICollection<UserArtist> Artists { get; set; }

        public ICollection<UserAlbum> Albums { get; set; }

        public ICollection<UserTrack> Tracks { get; set; }

        public ICollection<UserPlay> Plays { get; set; }

        public ICollection<UserCrown> Crowns { get; set; }

        public ICollection<GuildUser> GuildUsers { get; set; }

        public ICollection<GuildBlockedUser> GuildBlockedUsers { get; set; }

        public ICollection<FeaturedLog> FeaturedLogs { get; set; }
    }
}
