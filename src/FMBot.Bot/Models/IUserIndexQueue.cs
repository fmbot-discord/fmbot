using System;
using System.Collections.Generic;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Models
{
    public interface IUserIndexQueue
    {
        IObservable<User> UsersToIndex { get; }

        void Publish(IReadOnlyList<User> users);
    }
}
