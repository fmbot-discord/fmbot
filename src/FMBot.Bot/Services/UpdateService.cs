using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Domain.Models;
using FMBot.LastFM.Domain.Types;
using FMBot.LastFM.Repositories;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Serilog;

namespace FMBot.Bot.Services;

public class UpdateService : IUpdateService
{
    private readonly IUserUpdateQueue _userUpdateQueue;
    private readonly UpdateRepository _updateRepository;
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly IMemoryCache _cache;

    public UpdateService(IUserUpdateQueue userUpdateQueue, UpdateRepository updateRepository, IDbContextFactory<FMBotDbContext> contextFactory, IMemoryCache cache)
    {
        this._userUpdateQueue = userUpdateQueue;
        this._userUpdateQueue.UsersToUpdate.SubscribeAsync(OnNextAsync);
        this._updateRepository = updateRepository;
        this._contextFactory = contextFactory;
        this._cache = cache;
    }

    private async Task OnNextAsync(UpdateUserQueueItem user)
    {
        await this._updateRepository.UpdateUser(user);
    }

    public void AddUsersToUpdateQueue(IReadOnlyList<User> users)
    {
        Log.Information($"Adding {users.Count} users to update queue");

        this._userUpdateQueue.Publish(users.ToList());
    }

    public async Task<int> UpdateUser(User user)
    {
        var updatedUser = await this._updateRepository.UpdateUser(new UpdateUserQueueItem(user.UserId));
        return (int)updatedUser.Content.NewRecentTracksAmount;
    }

    public async Task<Response<RecentTrackList>> UpdateUserAndGetRecentTracks(User user)
    {
        if (this._cache.TryGetValue($"index-started-{user.UserId}", out bool _))
        {
            return new Response<RecentTrackList>
            {
                Success = false,
                Message =
                    "All your data is still being fetched from Last.fm for .fmbot. Please wait a moment for this to complete and try again.",
            };
        }

        return await this._updateRepository.UpdateUser(new UpdateUserQueueItem(user.UserId));
    }

    public async Task<IReadOnlyList<User>> GetOutdatedUsers(DateTime timeAuthorizedLastUpdated, DateTime timeUnauthorizedFilter)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var lastUsed = DateTime.UtcNow.AddMonths(-3);
        return await db.Users
            .AsQueryable()
            .Where(f => f.LastIndexed != null &&
                        f.LastUpdated != null &&
                        f.LastUsed != null &&
                        f.LastUsed > lastUsed &&
                        (f.SessionKeyLastFm != null && f.LastUpdated <= timeAuthorizedLastUpdated ||
                         f.SessionKeyLastFm == null && f.LastUpdated <= timeUnauthorizedFilter))
            .OrderBy(o => o.LastUpdated)
            .ToListAsync();
    }

    public async Task CorrectUserArtistPlaycount(int userId, string artistName, long correctPlaycount)
    {
        await this._updateRepository.CorrectUserArtistPlaycount(userId, artistName, correctPlaycount);
    }
    public async Task CorrectUserAlbumPlaycount(int userId, string artistName, string albumName, long correctPlaycount)
    {
        await this._updateRepository.CorrectUserAlbumPlaycount(userId, artistName, albumName, correctPlaycount);
    }
    public async Task CorrectUserTrackPlaycount(int userId, string artistName, string trackName, long correctPlaycount)
    {
        await this._updateRepository.CorrectUserTrackPlaycount(userId, artistName, trackName, correctPlaycount);
    }
}
