using System;
using System.Collections.Generic;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Interfaces;

public interface IUserIndexQueue
{
    IObservable<IndexUserQueueItem> UsersToIndex { get; }

    void Publish(IReadOnlyList<User> users);
}
