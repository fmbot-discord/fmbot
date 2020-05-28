using System;
using FMBot.Persistence.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace FMBot.Persistence.EntityFrameWork
{
    public class FMBotDbContext : DbContext
    {
        public FMBotDbContext()
        {
        }

        public FMBotDbContext(DbContextOptions<FMBotDbContext> options)
            : base(options)
        {
        }

        public virtual DbSet<Friend> Friends { get; set; }
        public virtual DbSet<Guild> Guilds { get; set; }
        public virtual DbSet<User> Users { get; set; }
        public virtual DbSet<Artist> Artists { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseNpgsql("Host=localhost;Port=5433;Username=postgres;Password=password;Database=fmbot;Command Timeout=15;Timeout=30;Persist Security Info=True");
                optionsBuilder.UseSnakeCaseNamingConvention();
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Friend>(entity =>
            {
                entity.HasKey(e => e.FriendId);

                entity.HasIndex(e => e.FriendUserId);

                entity.HasIndex(e => e.UserId);

                entity.HasOne(d => d.FriendUser)
                    .WithMany(p => p.FriendedByUsers)
                    .HasForeignKey(d => d.FriendUserId)
                    .HasConstraintName("FK.Friends.Users_FriendUserID");

                entity.HasOne(d => d.User)
                    .WithMany(p => p.Friends)
                    .HasForeignKey(d => d.UserId)
                    .HasConstraintName("FK.Friends.Users_UserID");
            });

            modelBuilder.Entity<Guild>(entity =>
            {
                entity.HasKey(e => e.GuildId);

                entity.Property(e => e.DisabledCommands)
                    .HasConversion(
                        v => string.Join(',', v),
                        v => v.Split(',', StringSplitOptions.RemoveEmptyEntries));

                entity.Property(e => e.EmoteReactions)
                    .HasConversion(
                        v => string.Join(',', v),
                        v => v.Split(',', StringSplitOptions.RemoveEmptyEntries));
            });

            modelBuilder.Entity<GuildUser>(entity =>
            {
                entity.HasKey(e => new { e.GuildId, e.UserId });

                entity.HasOne(sc => sc.Guild)
                    .WithMany(s => s.Users)
                    .HasForeignKey(sc => sc.GuildId);

                entity.HasOne(sc => sc.User)
                    .WithMany(s => s.Guilds)
                    .HasForeignKey(sc => sc.UserId);
            });

            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(e => e.UserId);
            });

            modelBuilder.Entity<Artist>(entity =>
            {
                entity.HasKey(a => a.ArtistId);

                entity.HasOne(u => u.User)
                    .WithMany(a => a.Artists)
                    .HasForeignKey(f => f.UserId);
            });

        }
    }
}
