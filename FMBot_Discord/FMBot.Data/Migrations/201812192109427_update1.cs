namespace FMBot.Data.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class update1 : DbMigration
    {
        public override void Up()
        {
            AddColumn("dbo.Users", "LastGeneratedChartDateTimeUtc", c => c.DateTime());
        }
        
        public override void Down()
        {
            DropColumn("dbo.Users", "LastGeneratedChartDateTimeUtc");
        }
    }
}
