using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Domain.Models;
using FMBot.LastFM.Services;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace FMBot.Bot.Services
{
    public class UpdateService : IUpdateService
    {
        private readonly IUserUpdateQueue _userUpdateQueue;
        private readonly GlobalUpdateService _globalUpdateService;
        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;

        public UpdateService(IUserUpdateQueue userUpdateQueue, GlobalUpdateService updateService, IDbContextFactory<FMBotDbContext> contextFactory)
        {
            this._userUpdateQueue = userUpdateQueue;
            this._userUpdateQueue.UsersToUpdate.SubscribeAsync(OnNextAsync);
            this._globalUpdateService = updateService;
            this._contextFactory = contextFactory;
        }

        private async Task OnNextAsync(UpdateUserQueueItem user)
        {
            await this._globalUpdateService.UpdateUser(user);
        }

        public void AddUsersToUpdateQueue(IReadOnlyList<User> users)
        {
            Log.Information($"Adding {users.Count} users to update queue");

            this._userUpdateQueue.Publish(users.ToList());
        }

        public async Task<int> UpdateUser(User user)
        {
            return await this._globalUpdateService.UpdateUser(new UpdateUserQueueItem(user.UserId));
        }

        public async Task<IReadOnlyList<User>> GetOutdatedUsers(DateTime timeAuthorizedLastUpdated, DateTime timeUnauthorizedFilter)
        {
            await using var db = this._contextFactory.CreateDbContext();
            return await db.Users
                    .AsQueryable()
                    .Where(f => f.LastIndexed != null &&
                                f.LastUpdated != null &&
                                (f.SessionKeyLastFm != null && f.LastUpdated <= timeAuthorizedLastUpdated ||
                                 f.SessionKeyLastFm == null && f.LastUpdated <= timeUnauthorizedFilter))
                    .OrderBy(o => o.LastUpdated)
                    .ToListAsync();
        }
    }
}
