namespace FMBot.Domain.Models
{
    public class UpdateUserQueueItem
    {
        public UpdateUserQueueItem(int userId, int timeoutMs = 0)
        {
            this.UserId = userId;
            this.TimeoutMs = timeoutMs;
        }

        public int UserId { get; }

        public int TimeoutMs { get; }
    }
}
