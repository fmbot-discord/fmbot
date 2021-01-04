using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FMBot.Domain.Models;
using FMBot.LastFM.Domain.Types;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Interfaces
{
    public interface IUpdateService
    {
        void AddUsersToUpdateQueue(IReadOnlyList<User> users);

        Task<int> UpdateUser(User user);

        Task<Response<RecentTrackList>> UpdateUserAndGetRecentTracks(User user);

        Task<IReadOnlyList<User>> GetOutdatedUsers(DateTime timeAuthorizedLastUpdated, DateTime timeUnauthorizedFilter);
    }
}
