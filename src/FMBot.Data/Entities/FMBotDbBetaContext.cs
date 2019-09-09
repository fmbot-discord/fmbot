using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

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
        public virtual DbSet<GuildUsers> GuildUsers { get; set; }
        public virtual DbSet<Guild> Guilds { get; set; }
        public virtual DbSet<User> Users { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (!optionsBuilder.IsConfigured)
            {
#warning To protect potentially sensitive information in your connection string, you should move it out of source code. See http://go.microsoft.com/fwlink/?LinkID=723263 for guidance on storing connection strings.
                optionsBuilder.UseSqlServer("Data Source=(LocalDB)\\FMBotDb;Initial Catalog=FMBotDb-Beta;Integrated Security=SSPI;");
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

                entity.Property(e => e.FriendID).HasColumnName("FriendID");

                entity.Property(e => e.FriendUserID).HasColumnName("FriendUserID");

                entity.Property(e => e.LastFMUserName).HasColumnName("LastFMUserName");

                entity.Property(e => e.UserID).HasColumnName("UserID");

                entity.HasOne(d => d.FriendUser)
                    .WithMany(p => p.FriendsFriendUser)
                    .HasForeignKey(d => d.FriendUserID)
                    .HasConstraintName("FK_dbo.Friends_dbo.Users_FriendUserID");

                entity.HasOne(d => d.User)
                    .WithMany(p => p.FriendsUser)
                    .HasForeignKey(d => d.UserID)
                    .HasConstraintName("FK_dbo.Friends_dbo.Users_UserID");
            });

            modelBuilder.Entity<GuildUsers>(entity =>
            {
                entity.HasKey(e => new { e.GuildID, e.UserID })
                    .HasName("PK_dbo.GuildUsers");

                entity.HasIndex(e => e.GuildID)
                    .HasName("IX_GuildID");

                entity.HasIndex(e => e.UserID)
                    .HasName("IX_UserID");

                entity.Property(e => e.GuildID).HasColumnName("GuildID");

                entity.Property(e => e.UserID).HasColumnName("UserID");

                entity.HasOne(d => d.Guild)
                    .WithMany(p => p.GuildUsers)
                    .HasForeignKey(d => d.GuildID)
                    .HasConstraintName("FK_dbo.GuildUsers_dbo.Guilds_GuildID");

                entity.HasOne(d => d.User)
                    .WithMany(p => p.GuildUsers)
                    .HasForeignKey(d => d.UserID)
                    .HasConstraintName("FK_dbo.GuildUsers_dbo.Users_UserID");
            });

            modelBuilder.Entity<Guild>(entity =>
            {
                entity.HasKey(e => e.GuildID)
                    .HasName("PK_dbo.Guilds");

                entity.Property(e => e.GuildID).HasColumnName("GuildID");

                entity.Property(e => e.DiscordGuildID).HasColumnName("DiscordGuildID");
            });

            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(e => e.UserID)
                    .HasName("PK_dbo.Users");

                entity.Property(e => e.UserID).HasColumnName("UserID");

                entity.Property(e => e.DiscordUserID).HasColumnName("DiscordUserID");

                entity.Property(e => e.LastGeneratedChartDateTimeUtc).HasColumnType("datetime");

                entity.Property(e => e.UserNameLastFM).HasColumnName("UserNameLastFM");
            });
        }
    }
}
