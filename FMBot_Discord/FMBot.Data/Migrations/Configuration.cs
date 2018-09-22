using System.Data.Entity.Migrations;

namespace FMBot.Data.Migrations
{
    internal sealed class Configuration : DbMigrationsConfiguration<FMBot.Data.Entities.FMBotDbContext>
    {
        public Configuration()
        {
            AutomaticMigrationsEnabled = false;
        }

        protected override void Seed(FMBot.Data.Entities.FMBotDbContext context)
        {
            //  This method will be called after migrating to the latest version.

            //  You can use the DbSet<T>.AddOrUpdate() helper extension method 
            //  to avoid creating duplicate seed data.
        }
    }
}
