namespace FMBot.Domain.Models
{
    public class IndexUserQueueItem
    {
        public IndexUserQueueItem(int userId, int timeoutMs = 0)
        {
            this.UserId = userId;
            this.TimeoutMs = timeoutMs;
        }

        public int UserId { get; }

        public int TimeoutMs { get; }
    }
}
