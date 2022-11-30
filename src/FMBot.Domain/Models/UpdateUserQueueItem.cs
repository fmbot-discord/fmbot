namespace FMBot.Domain.Models;

public class UpdateUserQueueItem
{
    public UpdateUserQueueItem(int userId, bool updateQueue = false, bool getAccurateTotalPlaycount = true)
    {
        this.UserId = userId;
        this.UpdateQueue = updateQueue;
        this.GetAccurateTotalPlaycount = getAccurateTotalPlaycount;
    }

    public int UserId { get; }

    public bool UpdateQueue { get; }

    public bool GetAccurateTotalPlaycount { get; }
}
