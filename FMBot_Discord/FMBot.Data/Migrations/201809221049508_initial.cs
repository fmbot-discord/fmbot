namespace FMBot.Data.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class initial : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.Guilds",
                c => new
                    {
                        GuildID = c.Int(nullable: false),
                        DiscordGuildID = c.String(),
                        Name = c.String(),
                    })
                .PrimaryKey(t => t.GuildID)
                .ForeignKey("dbo.Settings", t => t.GuildID)
                .Index(t => t.GuildID);
            
            CreateTable(
                "dbo.Settings",
                c => new
                    {
                        SettingID = c.Int(nullable: false, identity: true),
                        UserID = c.Int(nullable: false),
                        GuildID = c.Int(nullable: false),
                        UserNameLastFM = c.String(),
                        ChartType = c.Int(nullable: false),
                        ChartTimePeriod = c.Int(nullable: false),
                        TitlesEnabled = c.Boolean(nullable: false),
                    })
                .PrimaryKey(t => t.SettingID);
            
            CreateTable(
                "dbo.Users",
                c => new
                    {
                        UserID = c.Int(nullable: false),
                        DiscordUserID = c.String(),
                        Featured = c.Boolean(),
                        Blacklisted = c.Boolean(),
                        UserType = c.Int(nullable: false),
                    })
                .PrimaryKey(t => t.UserID)
                .ForeignKey("dbo.Settings", t => t.UserID)
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
            DropForeignKey("dbo.Users", "UserID", "dbo.Settings");
            DropForeignKey("dbo.Guilds", "GuildID", "dbo.Settings");
            DropIndex("dbo.GuildUsers", new[] { "UserID" });
            DropIndex("dbo.GuildUsers", new[] { "GuildID" });
            DropIndex("dbo.Users", new[] { "UserID" });
            DropIndex("dbo.Guilds", new[] { "GuildID" });
            DropTable("dbo.GuildUsers");
            DropTable("dbo.Users");
            DropTable("dbo.Settings");
            DropTable("dbo.Guilds");
        }
    }
}
