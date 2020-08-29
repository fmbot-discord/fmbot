using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Interfaces
{
    public interface IUpdateService
    {
        void AddUsersToUpdateQueue(IReadOnlyList<User> users);

        Task<int> UpdateUser(User user);

        Task<IReadOnlyList<User>> GetOutdatedUsers(DateTime timeLastUpdated);
    }
}
