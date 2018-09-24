using System.Data.Entity;

namespace FMBot.Data.Entities
{
    public class FMBotDbContext : DbContext
    {
        #region Constructor
        public FMBotDbContext()
            : base("FMBotDb")
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
        }



        public DbSet<Guild> Guilds { get; set; }

        public DbSet<User> Users { get; set; }

        public DbSet<Friend> Friends { get; set; }
        #endregion
    }
}
