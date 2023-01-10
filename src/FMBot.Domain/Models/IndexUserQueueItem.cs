namespace FMBot.Domain.Models;

public class IndexUserQueueItem
{
    public IndexUserQueueItem(int userId, bool indexQueue = false)
    {
        this.UserId = userId;
        this.IndexQueue = indexQueue;
    }

    public int UserId { get; }

    public bool IndexQueue { get; }
}
