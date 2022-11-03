using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using FMBot.Bot.Interfaces;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Models;

public class UserUpdateQueue : IUserUpdateQueue
{
    private readonly Subject<IReadOnlyList<UpdateUserQueueItem>> _subject;

    public UserUpdateQueue()
    {
        this._subject = new Subject<IReadOnlyList<UpdateUserQueueItem>>();
        this.UsersToUpdate = this._subject.SelectMany(q => q);
    }

    public IObservable<UpdateUserQueueItem> UsersToUpdate { get; }

    public void Publish(IReadOnlyList<User> users)
    {
        var queueItems = users
            .Select(s => new UpdateUserQueueItem(s.UserId, true))
            .ToList();

        this._subject.OnNext(queueItems);
    }
}
