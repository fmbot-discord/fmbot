using System;
using Microsoft.EntityFrameworkCore;

namespace FMBot.Data.Entities
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

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseNpgsql("Host=localhost;Port=5433;Username=postgres;Password=password;Database=fmbot;Command Timeout=15;Timeout=30");
            }
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasAnnotation("ProductVersion", "2.2.6-servicing-10079");

            modelBuilder.Entity<Friend>(entity =>
            {
                entity.HasKey(e => e.FriendID)
                    .HasName("PK_dbo.Friends");

                entity.HasIndex(e => e.FriendUserID)
                    .HasName("IX_FriendUserID");

                entity.HasIndex(e => e.UserID)
                    .HasName("IX_UserID");

                entity.HasOne(d => d.FriendUser)
                    .WithMany(p => p.FriendsFriendUser)
                    .HasForeignKey(d => d.FriendUserID)
                    .HasConstraintName("FK_dbo.Friends_dbo.Users_FriendUserID");

                entity.HasOne(d => d.User)
                    .WithMany(p => p.FriendsUser)
                    .HasForeignKey(d => d.UserID)
                    .HasConstraintName("FK_dbo.Friends_dbo.Users_UserID");
            });

            modelBuilder.Entity<Guild>(entity =>
            {
                entity.HasKey(e => e.GuildID)
                    .HasName("PK_dbo.Guilds");

                entity.Property(e => e.EmoteReactions)
                    .HasConversion(
                        v => string.Join(',', v),
                        v => v.Split(',', StringSplitOptions.RemoveEmptyEntries));
            });

            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(e => e.UserID)
                    .HasName("PK_dbo.Users");
            });
        }
    }
}
