namespace FMBot.Data.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class update : DbMigration
    {
        public override void Up()
        {
            DropForeignKey("dbo.Guilds", "GuildID", "dbo.Settings");
            DropForeignKey("dbo.Users", "UserID", "dbo.Settings");
            DropIndex("dbo.Users", new[] { "UserID" });
            DropIndex("dbo.Guilds", new[] { "GuildID" });
            AddColumn("dbo.Users", "TitlesEnabled", c => c.Boolean());
            AddColumn("dbo.Users", "UserNameLastFM", c => c.String());
            AddColumn("dbo.Users", "ChartType", c => c.Int(nullable: false));
            AddColumn("dbo.Users", "ChartTimePeriod", c => c.Int(nullable: false));
            AddColumn("dbo.Guilds", "Blacklisted", c => c.Boolean());
            AddColumn("dbo.Guilds", "TitlesEnabled", c => c.Boolean());
            AddColumn("dbo.Guilds", "ChartType", c => c.Int(nullable: false));
            AddColumn("dbo.Guilds", "ChartTimePeriod", c => c.Int(nullable: false));
            DropTable("dbo.Settings");
        }
        
        public override void Down()
        {
            CreateTable(
                "dbo.Settings",
                c => new
                    {
                        SettingID = c.Int(nullable: false, identity: true),
                        UserID = c.Int(),
                        GuildID = c.Int(),
                        UserNameLastFM = c.String(),
                        ChartType = c.Int(nullable: false),
                        ChartTimePeriod = c.Int(nullable: false),
                        TitlesEnabled = c.Boolean(nullable: false),
                    })
                .PrimaryKey(t => t.SettingID);
            
            DropColumn("dbo.Guilds", "ChartTimePeriod");
            DropColumn("dbo.Guilds", "ChartType");
            DropColumn("dbo.Guilds", "TitlesEnabled");
            DropColumn("dbo.Guilds", "Blacklisted");
            DropColumn("dbo.Users", "ChartTimePeriod");
            DropColumn("dbo.Users", "ChartType");
            DropColumn("dbo.Users", "UserNameLastFM");
            DropColumn("dbo.Users", "TitlesEnabled");
            CreateIndex("dbo.Guilds", "GuildID");
            CreateIndex("dbo.Users", "UserID");
            AddForeignKey("dbo.Users", "UserID", "dbo.Settings", "SettingID");
            AddForeignKey("dbo.Guilds", "GuildID", "dbo.Settings", "SettingID");
        }
    }
}
