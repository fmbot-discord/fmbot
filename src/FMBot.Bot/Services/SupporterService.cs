using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using FMBot.Bot.Configurations;
using FMBot.Domain;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;

namespace FMBot.Bot.Services
{
    public class SupporterService
    {
        public async Task<string> GetRandomSupporter(IGuild guild)
        {
            if (guild == null)
            {
                return null;
            }

            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);
            var guildSettings = await db.Guilds
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordGuildId == guild.Id);

            if (guildSettings != null && guildSettings.DisableSupporterMessages == true)
            {
                return null;
            }

            var rnd = new Random();
            var randomHintNumber = rnd.Next(0, Constants.SupporterMessageChance);

            if (randomHintNumber == 1)
            {
                var supporters = db.Supporters
                    .AsQueryable()
                    .Where(w => w.SupporterMessagesEnabled)
                    .ToList();

                if (!supporters.Any())
                {
                    return null;
                }

                var rand = new Random();
                var randomSupporter = supporters[rand.Next(supporters.Count)];

                return randomSupporter.Name;
            }

            return null;
        }

        public async Task<IReadOnlyList<Supporter>> GetAllVisibleSupporters()
        {
            await using var db = new FMBotDbContext(ConfigData.Data.Database.ConnectionString);

            return await db.Supporters
                .AsQueryable()
                .Where(w => w.VisibleInOverview)
                .ToListAsync();
        }
    }
}
