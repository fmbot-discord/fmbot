using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Models
{
    public class UserIndexQueue : IUserIndexQueue
    {
        private readonly Subject<IReadOnlyList<User>> subject;

        public UserIndexQueue()
        {
            this.subject = new Subject<IReadOnlyList<User>>();
            this.UsersToIndex = this.subject.SelectMany(q => q);
        }

        public IObservable<User> UsersToIndex { get; }

        public void Publish(IReadOnlyList<User> users)
        {
            this.subject.OnNext(users);
        }
    }
}
