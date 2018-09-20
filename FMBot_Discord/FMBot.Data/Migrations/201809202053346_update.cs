namespace FMBot.Data.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class update : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.Guilds",
                c => new
                    {
                        GuildID = c.Int(nullable: false, identity: true),
                        DiscordGuildID = c.Int(nullable: false),
                        Name = c.String(),
                    })
                .PrimaryKey(t => t.GuildID);
            
            CreateTable(
                "dbo.Users",
                c => new
                    {
                        UserID = c.Int(nullable: false, identity: true),
                        DiscordUserID = c.Int(nullable: false),
                    })
                .PrimaryKey(t => t.UserID);
            
            CreateTable(
                "dbo.UserSettings",
                c => new
                    {
                        UserID = c.Int(nullable: false),
                        UserNameLastFM = c.String(),
                        ChartType = c.Int(nullable: false),
                        ChartTimePeriod = c.Int(nullable: false),
                        TitlesEnabled = c.Boolean(nullable: false),
                    })
                .PrimaryKey(t => t.UserID)
                .ForeignKey("dbo.Users", t => t.UserID)
                .Index(t => t.UserID);
            
            CreateTable(
                "dbo.GuildUsers",
                c => new
                    {
                        GuildID = c.Int(nullable: false),
                        UserID = c.Int(nullable: false),
                    })
                .PrimaryKey(t => new { t.GuildID, t.UserID })
                .ForeignKey("dbo.Guilds", t => t.GuildID, cascadeDelete: true)
                .ForeignKey("dbo.Users", t => t.UserID, cascadeDelete: true)
                .Index(t => t.GuildID)
                .Index(t => t.UserID);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.GuildUsers", "UserID", "dbo.Users");
            DropForeignKey("dbo.GuildUsers", "GuildID", "dbo.Guilds");
            DropForeignKey("dbo.UserSettings", "UserID", "dbo.Users");
            DropIndex("dbo.GuildUsers", new[] { "UserID" });
            DropIndex("dbo.GuildUsers", new[] { "GuildID" });
            DropIndex("dbo.UserSettings", new[] { "UserID" });
            DropTable("dbo.GuildUsers");
            DropTable("dbo.UserSettings");
            DropTable("dbo.Users");
            DropTable("dbo.Guilds");
        }
    }
}
