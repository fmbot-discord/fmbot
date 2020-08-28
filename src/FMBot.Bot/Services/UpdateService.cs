using System.Threading.Tasks;
using FMBot.Bot.Interfaces;
using FMBot.LastFM.Services;
using FMBot.Persistence.Domain.Models;
using Serilog;

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
            Log.Information("Starting index for {UserNameLastFM}", user.UserNameLastFM);

            return await this._globalUpdateService.UpdateUser(user);
        }
    }
}
