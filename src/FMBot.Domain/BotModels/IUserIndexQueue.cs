using System;
using System.Collections.Generic;
using FMBot.Domain.DatabaseModels;

namespace FMBot.Domain.BotModels
{
    public interface IUserIndexQueue
    {
        IObservable<User> UsersToIndex { get; }

        void Publish(IReadOnlyList<User> users);
    }
}
