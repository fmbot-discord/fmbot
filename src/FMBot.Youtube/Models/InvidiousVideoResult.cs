namespace FMBot.Youtube.Models
{
    public class InvidiousVideoResult
    {
        public string Title { get; set; }
        public string VideoId { get; set; }
        public string Description { get; set; }
        public string PublishedText { get; set; }
        public int ViewCount { get; set; }
        public int LikeCount { get; set; }
        public bool IsFamilyFriendly { get; set; }
        public string SubCountText { get; set; }
    }
}
