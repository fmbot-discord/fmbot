using System.Data.Entity;

namespace FMBot.Data.Entities
{
    public class FMBotDbContext : DbContext
    {
        #region Constructor
        public FMBotDbContext()
            : base("FMBotDbConnection")
        {
        }
        #endregion

        #region DB Creation

        public static FMBotDbContext Create()
        {
            return new FMBotDbContext();
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

            modelBuilder.Entity<Settings>()
                .HasOptional(s => s.User)
                .WithRequired(ad => ad.Settings);


            modelBuilder.Entity<Settings>()
                .HasOptional(s => s.Guild)
                .WithRequired(ad => ad.Settings);
        }



        public DbSet<Guild> Guilds { get; set; }

        public DbSet<User> Users { get; set; }

        public DbSet<Settings> Settings { get; set; }
        #endregion
    }
}
