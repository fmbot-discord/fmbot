using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
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

        private async Task OnNextAsync(User user)
        {
            Log.Verbose("User next up for update is {UserNameLastFM}", user.UserNameLastFM);
            await this._globalUpdateService.UpdateUser(user);
        }

        public void AddUsersToUpdateQueue(IReadOnlyList<User> users)
        {
            Log.Information($"Adding {users.Count} users to update queue");

            this._userUpdateQueue.Publish(users.ToList());
        }

        public async Task<int> UpdateUser(User user)
        {
            Log.Information("Starting update for {UserNameLastFM}", user.UserNameLastFM);

            return await this._globalUpdateService.UpdateUser(user);
        }

        public async Task<IReadOnlyList<User>> GetOutdatedUsers(DateTime timeLastUpdated)
        {
            await using var db = this._contextFactory.CreateDbContext();
            return await db.Users
                    .AsQueryable()
                    .Where(f => f.LastIndexed != null &&
                                f.LastUpdated != null &&
                                f.LastUpdated <= timeLastUpdated)
                    .ToListAsync();
        }
    }
}
