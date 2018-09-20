using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FMBot.Data.Entities
{
    public class FMBotDbContext : DbContext
    {
        public FMBotDbContext ()
            : base("FMBotDbConnection")
        {
        }

        public static FMBotDbContext  Create()
        {
            return new FMBotDbContext ();
        }

        protected override void OnModelCreating(DbModelBuilder modelBuilder)
        {
            Database.SetInitializer(new MigrateDatabaseToLatestVersion<FMBotDbContext, Migrations.Configuration>());

            base.OnModelCreating(modelBuilder);


            modelBuilder.Entity<Guild>()
                .HasMany<User>(s => s.Users)
                .WithMany(c => c.Guilds)
                .Map(cs =>
                {
                    cs.MapLeftKey("GuildID");
                    cs.MapRightKey("UserID");
                    cs.ToTable("GuildUsers");
                });
        }



        public DbSet<Guild> Guilds { get; set; }

        public DbSet<User> Users { get; set; }

        public DbSet<UserSetting> UserSettings { get; set; }
    }
}
