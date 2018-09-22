namespace FMBot.Data.Migrations
{
    using System;
    using System.Data.Entity.Migrations;
    
    public partial class update : DbMigration
    {
        public override void Up()
        {
            CreateTable(
                "dbo.Friends",
                c => new
                    {
                        UserID = c.Int(nullable: false),
                        LastFMUserName = c.String(),
                        FriendUserID = c.Int(),
                    })
                .PrimaryKey(t => t.UserID)
                .ForeignKey("dbo.Users", t => t.FriendUserID)
                .ForeignKey("dbo.Users", t => t.UserID)
                .Index(t => t.UserID)
                .Index(t => t.FriendUserID);
            
        }
        
        public override void Down()
        {
            DropForeignKey("dbo.Friends", "UserID", "dbo.Users");
            DropForeignKey("dbo.Friends", "FriendUserID", "dbo.Users");
            DropIndex("dbo.Friends", new[] { "FriendUserID" });
            DropIndex("dbo.Friends", new[] { "UserID" });
            DropTable("dbo.Friends");
        }
    }
}
