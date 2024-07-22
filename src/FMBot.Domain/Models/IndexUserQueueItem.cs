namespace FMBot.Domain.Models;

public class IndexUserQueueItem
{
    public IndexUserQueueItem(int userId, bool indexQueue = false)
    {
        this.UserId = userId;
        this.IndexQueue = indexQueue;
    }

    public IndexUserQueueItem()
    {
    }

    public int UserId { get; set;  }

    public bool IndexQueue { get; set; }
}
