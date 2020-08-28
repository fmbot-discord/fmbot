using System;
using System.Threading.Tasks;
using FMBot.Bot.Interfaces;
using FMBot.LastFM.Services;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Services
{
    public class UpdateService : IUpdateService
    {
        private readonly GlobalUpdateService _globalUpdateService;

        public UpdateService(GlobalUpdateService updateService)
        {
            this._globalUpdateService = updateService;
        }

        public async Task<int> UpdateUser(User user)
        {
            Console.WriteLine($"Starting update for {user.UserNameLastFM}");

            return await this._globalUpdateService.UpdateUser(user);
        }
    }
}
