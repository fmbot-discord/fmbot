using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
using FMBot.Bot.Interfaces;
using FMBot.Bot.Models;
using FMBot.Bot.Resources;
using FMBot.LastFM.Services;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PostgreSQLCopyHelper;

namespace FMBot.Bot.Services
{
    public class UpdateService : IUpdateService
    {
        private readonly GlobalUpdateService _globalUpdateService;

        public UpdateService(GlobalUpdateService updateService)
        {
            this._globalUpdateService = updateService;
        }

        public async Task UpdateUser(User user)
        {
            Console.WriteLine($"Starting update for {user.UserNameLastFM}");

            await this._globalUpdateService.UpdateUser(user);
        }
    }
}
