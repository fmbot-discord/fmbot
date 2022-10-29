using System;
using System.Collections.Generic;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Interfaces;

public interface IUserUpdateQueue
{
    IObservable<UpdateUserQueueItem> UsersToUpdate { get; }

    void Publish(IReadOnlyList<User> users);
}
