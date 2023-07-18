using System;
using System.Collections.Generic;
using System.Linq;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Configuration;

namespace FMBot.Persistence.EntityFrameWork
{
    public class FMBotDbContext : DbContext
    {
        public virtual DbSet<Friend> Friends { get; set; }
        public virtual DbSet<Guild> Guilds { get; set; }
        public virtual DbSet<Channel> Channels { get; set; }
        public virtual DbSet<Webhook> Webhooks { get; set; }

        public virtual DbSet<GuildBlockedUser> GuildBlockedUsers { get; set; }
        public virtual DbSet<GuildUser> GuildUsers { get; set; }
        public virtual DbSet<User> Users { get; set; }
        public virtual DbSet<Supporter> Supporters { get; set; }

        public virtual DbSet<DiscogsRelease> DiscogsReleases { get; set; }
        public virtual DbSet<UserDiscogsReleases> UserDiscogsReleases { get; set; }
        public virtual DbSet<UserDiscogs> UserDiscogs { get; set; }

        public virtual DbSet<BottedUser> BottedUsers { get; set; }
        public virtual DbSet<InactiveUsers> InactiveUsers { get; set; }
        public virtual DbSet<BottedUserReport> BottedUserReport { get; set; }
        public virtual DbSet<CensoredMusicReport> CensoredMusicReport { get; set; }

        public virtual DbSet<UserArtist> UserArtists { get; set; }
        public virtual DbSet<UserAlbum> UserAlbums { get; set; }
        public virtual DbSet<UserTrack> UserTracks { get; set; }
        public virtual DbSet<UserPlay> UserPlays { get; set; }
        public virtual DbSet<UserCrown> UserCrowns { get; set; }
        public virtual DbSet<UserStreak> UserStreaks { get; set; }
        public virtual DbSet<AiGeneration> AiGenerations { get; set; }

        public virtual DbSet<Artist> Artists { get; set; }
        public virtual DbSet<Album> Albums { get; set; }
        public virtual DbSet<Track> Tracks { get; set; }

        public virtual DbSet<CensoredMusic> CensoredMusic { get; set; }
        public virtual DbSet<FeaturedLog> FeaturedLogs { get; set; }

        public virtual DbSet<ArtistGenre> ArtistGenres { get; set; }
        public virtual DbSet<ArtistAlias> ArtistAliases { get; set; }

        private readonly IConfiguration _configuration;

        public FMBotDbContext(IConfiguration configuration)
        {
            this._configuration = configuration;
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseNpgsql(this._configuration["Database:ConnectionString"]);

                // Uncomment below connection string when creating migrations, and also comment out the above iconfiguration stuff
                //optionsBuilder.UseNpgsql("Host=localhost;Port=5433;Username=postgres;Password=password;Database=fmbot;Command Timeout=60;Timeout=60;Persist Security Info=True");

                optionsBuilder.UseSnakeCaseNamingConvention();
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasPostgresExtension("citext");
            modelBuilder.HasPostgresExtension("pg_trgm");

            modelBuilder.Entity<Friend>(entity =>
            {
                entity.HasKey(e => e.FriendId);

                entity.HasOne(d => d.FriendUser)
                    .WithMany(p => p.FriendedByUsers)
                    .HasForeignKey(d => d.FriendUserId)
                    .HasConstraintName("FK.Friends.Users_FriendUserID")
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(d => d.User)
                    .WithMany(p => p.Friends)
                    .HasForeignKey(d => d.UserId)
                    .HasConstraintName("FK.Friends.Users_UserID")
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Guild>(entity =>
            {
                entity.HasKey(e => e.GuildId);

                entity.HasIndex(i => i.DiscordGuildId);

                entity.Property(e => e.DisabledCommands)
                    .HasConversion(
                        v => string.Join(',', v),
                        v => v.Split(',', StringSplitOptions.RemoveEmptyEntries));

                entity.Property(e => e.EmoteReactions)
                    .HasConversion(
                        v => string.Join(',', v),
                        v => v.Split(',', StringSplitOptions.RemoveEmptyEntries));
            });

            modelBuilder.Entity<Channel>(entity =>
            {
                entity.HasKey(e => e.ChannelId);

                entity.HasIndex(i => i.DiscordChannelId);

                entity.HasIndex(i => i.GuildId);

                entity.HasOne(sc => sc.Guild)
                    .WithMany(s => s.Channels)
                    .HasForeignKey(sc => sc.GuildId);

                entity.Property(e => e.DisabledCommands)
                    .HasConversion(
                        v => string.Join(',', v),
                        v => v.Split(',', StringSplitOptions.RemoveEmptyEntries));
            });

            modelBuilder.Entity<GuildUser>(entity =>
            {
                entity.HasKey(e => new { e.GuildId, e.UserId });

                entity.HasOne(sc => sc.Guild)
                    .WithMany(s => s.GuildUsers)
                    .HasForeignKey(sc => sc.GuildId);

                entity.HasOne(sc => sc.User)
                    .WithMany(s => s.GuildUsers)
                    .HasForeignKey(sc => sc.UserId);
            });

            modelBuilder.Entity<GuildBlockedUser>(entity =>
            {
                entity.HasKey(e => new { e.GuildId, e.UserId });

                entity.HasOne(sc => sc.Guild)
                    .WithMany(s => s.GuildBlockedUsers)
                    .HasForeignKey(sc => sc.GuildId);

                entity.HasOne(sc => sc.User)
                    .WithMany(s => s.GuildBlockedUsers)
                    .HasForeignKey(sc => sc.UserId);
            });

            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(e => e.UserId);

                entity.HasIndex(i => i.UserId);

                entity.HasIndex(i => i.DiscordUserId);

                entity.Property(e => e.EmoteReactions)
                    .HasConversion(
                        v => string.Join(',', v),
                        v => v.Split(',', StringSplitOptions.RemoveEmptyEntries));
            });

            modelBuilder.Entity<Supporter>(entity =>
            {
                entity.HasKey(e => e.SupporterId);
            });

            modelBuilder.Entity<BottedUser>(entity =>
            {
                entity.HasKey(e => e.BottedUserId);
            });

            modelBuilder.Entity<BottedUserReport>(entity =>
            {
                entity.HasKey(e => e.Id);
            });

            modelBuilder.Entity<InactiveUsers>(entity =>
            {
                entity.HasKey(e => e.InactiveUserId);

                entity.HasIndex(i => i.UserId);

                entity
                    .HasOne<User>()
                    .WithMany()
                    .HasForeignKey(p => p.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<UserArtist>(entity =>
            {
                entity.HasKey(a => a.UserArtistId);

                entity.HasIndex(i => i.UserId);

                entity.Property(e => e.Name)
                    .HasColumnType("citext");

                entity.HasOne(u => u.User)
                    .WithMany(a => a.Artists)
                    .HasForeignKey(f => f.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<UserAlbum>(entity =>
            {
                entity.HasKey(a => a.UserAlbumId);

                entity.HasIndex(i => i.UserId);

                entity.Property(e => e.Name)
                    .HasColumnType("citext");

                entity.Property(e => e.ArtistName)
                    .HasColumnType("citext");

                entity.HasOne(u => u.User)
                    .WithMany(a => a.Albums)
                    .HasForeignKey(f => f.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<UserTrack>(entity =>
            {
                entity.HasKey(a => a.UserTrackId);

                entity.HasIndex(i => i.UserId);

                entity.Property(e => e.Name)
                    .HasColumnType("citext");

                entity.Property(e => e.ArtistName)
                    .HasColumnType("citext");

                entity.HasOne(u => u.User)
                    .WithMany(a => a.Tracks)
                    .HasForeignKey(f => f.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<UserPlay>(entity =>
            {
                entity.HasKey(a => a.UserPlayId);

                entity.HasIndex(i => i.UserId);

                entity.Property(e => e.TrackName)
                    .HasColumnType("citext");

                entity.Property(e => e.AlbumName)
                    .HasColumnType("citext");

                entity.Property(e => e.ArtistName)
                    .HasColumnType("citext");

                entity.HasOne(u => u.User)
                    .WithMany(a => a.Plays)
                    .HasForeignKey(f => f.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<UserCrown>(entity =>
            {
                entity.HasKey(e => e.CrownId);

                entity.HasIndex(i => i.UserId);

                entity.HasIndex(i => i.GuildId);

                entity.Property(e => e.ArtistName)
                    .HasColumnType("citext");

                entity.HasOne(sc => sc.Guild)
                    .WithMany(s => s.GuildCrowns)
                    .HasForeignKey(sc => sc.GuildId);

                entity.HasOne(sc => sc.User)
                    .WithMany(s => s.Crowns)
                    .HasForeignKey(sc => sc.UserId);
            });

            modelBuilder.Entity<UserStreak>(entity =>
            {
                entity.HasKey(e => e.UserStreakId);

                entity.HasIndex(i => i.UserId);

                entity.Property(e => e.ArtistName)
                    .HasColumnType("citext");

                entity.Property(e => e.AlbumName)
                    .HasColumnType("citext");

                entity.Property(e => e.TrackName)
                    .HasColumnType("citext");

                entity.HasOne(u => u.User)
                    .WithMany(a => a.Streaks)
                    .HasForeignKey(f => f.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<AiGeneration>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.HasIndex(i => i.UserId);

                entity.HasOne(u => u.User)
                    .WithMany(a => a.AiGenerations)
                    .HasForeignKey(f => f.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<FeaturedLog>(entity =>
            {
                entity.HasKey(e => e.FeaturedLogId);

                entity.HasIndex(i => i.UserId);

                entity.Property(e => e.ArtistName)
                    .HasColumnType("citext");

                entity.Property(e => e.AlbumName)
                    .HasColumnType("citext");

                entity.Property(e => e.TrackName)
                    .HasColumnType("citext");

                entity.HasOne(sc => sc.User)
                    .WithMany(s => s.FeaturedLogs)
                    .HasForeignKey(sc => sc.UserId);
            });

            modelBuilder.Entity<Artist>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.Name)
                    .HasColumnType("citext");
            });

            modelBuilder.Entity<Album>(entity =>
            {
                entity.HasKey(a => a.Id);

                entity.Property(e => e.Name)
                    .HasColumnType("citext");

                entity.Property(e => e.ArtistName)
                    .HasColumnType("citext");

                entity.HasOne(d => d.Artist)
                    .WithMany(p => p.Albums)
                    .HasForeignKey(d => d.ArtistId);
            });

            modelBuilder.Entity<Track>(entity =>
            {
                entity.HasKey(a => a.Id);

                entity.Property(e => e.Name)
                    .HasColumnType("citext");

                entity.Property(e => e.AlbumName)
                    .HasColumnType("citext");

                entity.Property(e => e.ArtistName)
                    .HasColumnType("citext");

                entity.HasOne(d => d.Artist)
                    .WithMany(p => p.Tracks)
                    .HasForeignKey(d => d.ArtistId);

                entity.HasOne(d => d.Album)
                    .WithMany(p => p.Tracks)
                    .HasForeignKey(d => d.AlbumId);
            });

            modelBuilder.Entity<CensoredMusic>(entity =>
            {
                entity.HasKey(a => a.CensoredMusicId);
            });

            modelBuilder.Entity<CensoredMusicReport>(entity =>
            {
                entity.HasKey(a => a.Id);
            });

            modelBuilder.Entity<ArtistAlias>(entity =>
            {
                entity.HasKey(a => a.Id);

                entity.HasOne(d => d.Artist)
                    .WithMany(p => p.ArtistAliases)
                    .HasForeignKey(d => d.ArtistId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ArtistGenre>(entity =>
            {
                entity.HasKey(a => a.Id);

                entity.Property(e => e.Name)
                    .HasColumnType("citext");

                entity.HasOne(d => d.Artist)
                    .WithMany(p => p.ArtistGenres)
                    .HasForeignKey(d => d.ArtistId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<UserDiscogs>(entity =>
            {
                entity.HasKey(a => a.UserId);

                entity.HasOne(u => u.User)
                    .WithOne(a => a.UserDiscogs)
                    .HasForeignKey<UserDiscogs>(f => f.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<UserDiscogsReleases>(entity =>
            {
                entity.HasKey(a => a.UserDiscogsReleaseId);

                entity.HasIndex(i => i.UserId);

                entity.HasOne(u => u.User)
                    .WithMany(a => a.DiscogsReleases)
                    .HasForeignKey(f => f.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(d => d.Release)
                    .WithMany(p => p.UserDiscogsReleases)
                    .HasForeignKey(d => d.ReleaseId);
            });

            modelBuilder.Entity<DiscogsRelease>(entity =>
            {
                entity.HasKey(a => a.DiscogsId);

                entity.Property(p => p.DiscogsId)
                    .ValueGeneratedNever();

                entity.Property(e => e.Format)
                    .HasColumnType("citext");

                entity.Property(e => e.Label)
                    .HasColumnType("citext");

                entity.Property(e => e.Title)
                    .HasColumnType("citext");

                entity.Property(e => e.Artist)
                    .HasColumnType("citext");

                entity.Property(e => e.FeaturingArtist)
                    .HasColumnType("citext");
            });

            modelBuilder.Entity<DiscogsFormatDescriptions>(entity =>
            {
                entity.HasKey(a => a.Id);

                entity.Property(e => e.Description)
                    .HasColumnType("citext");

                entity.HasOne(d => d.DiscogsRelease)
                    .WithMany(p => p.FormatDescriptions)
                    .HasForeignKey(d => d.ReleaseId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<DiscogsStyle>(entity =>
            {
                entity.HasKey(a => a.Id);

                entity.Property(e => e.Description)
                    .HasColumnType("citext");

                entity.HasOne(d => d.DiscogsRelease)
                    .WithMany(p => p.Styles)
                    .HasForeignKey(d => d.ReleaseId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<DiscogsGenre>(entity =>
            {
                entity.HasKey(a => a.Id);

                entity.Property(e => e.Description)
                    .HasColumnType("citext");

                entity.HasOne(d => d.DiscogsRelease)
                    .WithMany(p => p.Genres)
                    .HasForeignKey(d => d.ReleaseId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<Webhook>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.HasOne(sc => sc.Guild)
                    .WithMany(s => s.Webhooks)
                    .HasForeignKey(sc => sc.GuildId);
            });
        }
    }
}
