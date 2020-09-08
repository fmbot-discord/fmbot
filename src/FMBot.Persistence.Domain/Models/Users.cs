using System;
using System.Collections.Generic;
using FMBot.Domain.Models;

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

        public string SessionKeyLastFm { get; set; }

        public FmEmbedType FmEmbedType { get; set; }

        public FmCountType? FmCountType { get; set; }

        public ChartTimePeriod ChartTimePeriod { get; set; }

        public DateTime? LastGeneratedChartDateTimeUtc { get; set; }

        public DateTime? LastIndexed { get; set; }

        public DateTime? LastUpdated { get; set; }

        public DateTime? LastScrobbleUpdate { get; set; }

        public ICollection<Friend> FriendedByUsers { get; set; }

        public ICollection<Friend> Friends { get; set; }

        public ICollection<UserArtist> Artists { get; set; }

        public ICollection<UserAlbum> Albums { get; set; }

        public ICollection<UserTrack> Tracks { get; set; }

        public ICollection<GuildUser> GuildUsers { get; set; }
    }
}
