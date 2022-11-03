namespace FMBot.Domain.Models;

public class UpdateUserQueueItem
{
    public UpdateUserQueueItem(int userId, bool updateQueue = false)
    {
        this.UserId = userId;
        this.UpdateQueue = updateQueue;
    }

    public int UserId { get; }

    public bool UpdateQueue { get; }
}
