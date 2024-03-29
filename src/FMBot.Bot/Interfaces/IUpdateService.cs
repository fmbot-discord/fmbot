using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FMBot.Domain.Models;
using FMBot.Domain.Types;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Interfaces;

public interface IUpdateService
{
    void AddUsersToUpdateQueue(IReadOnlyList<User> users);

    Task<Response<RecentTrackList>> UpdateUser(UpdateUserQueueItem queueItem);
    Task<int> UpdateUser(User user);

    Task<Response<RecentTrackList>> UpdateUserAndGetRecentTracks(User user, bool bypassIndexPending = false);

    Task<IReadOnlyList<User>> GetOutdatedUsers(DateTime timeAuthorizedLastUpdated, DateTime timeUnauthorizedFilter);

    Task CorrectUserArtistPlaycount(int userId, string artistName, long correctPlaycount);
    Task CorrectUserAlbumPlaycount(int userId, string artistName, string albumName, long correctPlaycount);
    Task CorrectUserTrackPlaycount(int userId, string artistName, string trackName, long correctPlaycount);
}
