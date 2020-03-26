namespace FMBot.DbMigration.OldDatabase.Entities
{
    public partial class Friend
    {
        public int FriendID { get; set; }
        public int UserID { get; set; }
        public string LastFMUserName { get; set; }
        public int? FriendUserID { get; set; }

        public virtual User FriendUser { get; set; }
        public virtual User User { get; set; }
    }
}
