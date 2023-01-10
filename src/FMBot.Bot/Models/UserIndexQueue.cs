using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using FMBot.Bot.Interfaces;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Models;

public class UserIndexQueue : IUserIndexQueue
{
    private readonly Subject<IReadOnlyList<IndexUserQueueItem>> _subject;

    public UserIndexQueue()
    {
        this._subject = new Subject<IReadOnlyList<IndexUserQueueItem>>();
        this.UsersToIndex = this._subject.SelectMany(q => q);
    }

    public IObservable<IndexUserQueueItem> UsersToIndex { get; }

    public void Publish(IReadOnlyList<User> users)
    {
        var queueItems = users
            .Select(s => new IndexUserQueueItem(s.UserId, true))
            .ToList();

        this._subject.OnNext(queueItems);
    }
}
