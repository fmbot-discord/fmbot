namespace FMBot.Domain.Models;

public class UpdateUserQueueItem
{
    public UpdateUserQueueItem(int userId, bool updateQueue = false, bool getAccurateTotalPlaycount = true)
    {
        this.UserId = userId;
        this.UpdateQueue = updateQueue;
        this.GetAccurateTotalPlaycount = getAccurateTotalPlaycount;
    }

    public UpdateUserQueueItem()
    {
    }

    public int UserId { get; set; }

    public bool UpdateQueue { get; set;  }

    public bool GetAccurateTotalPlaycount { get; set; }
}
