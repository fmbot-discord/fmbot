using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using FMBot.Bot.Interfaces;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Models
{
    public class UserUpdateQueue : IUserUpdateQueue
    {
        private readonly Subject<IReadOnlyList<User>> _subject;

        public UserUpdateQueue()
        {
            this._subject = new Subject<IReadOnlyList<User>>();
            this.UsersToUpdate = this._subject.SelectMany(q => q);
        }

        public IObservable<User> UsersToUpdate { get; }

        public void Publish(IReadOnlyList<User> users)
        {
            this._subject.OnNext(users);
        }
    }
}
