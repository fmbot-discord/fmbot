using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using FMBot.Persistence.EntityFrameWork.Migrations;
using Google.Apis.Testing;
using Microsoft.EntityFrameworkCore;

namespace FMBot.Bot.Services
{
    public class SupporterService
    {
        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;

        public SupporterService(IDbContextFactory<FMBotDbContext> contextFactory)
        {
            this._contextFactory = contextFactory;
        }

        public async Task<string> GetRandomSupporter(IGuild guild)
        {
            if (guild == null)
            {
                return null;
            }

            await using var db = this._contextFactory.CreateDbContext();
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
                var randomSupporter = supporters[rand.Next(supporters.Count())];

                return randomSupporter.Name;
            }

            return null;
        }

        public async Task<Supporter> AddSupporter(ulong id, string name, string notes)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var user = await db.Users
                .AsQueryable()
                .FirstOrDefaultAsync(f => f.DiscordUserId == id);

            user.UserType = UserType.Backer;
            db.Update(user);

            var supporterToAdd = new Supporter
            {
                Name = name,
                Created = DateTime.UtcNow,
                Notes = notes,
                SupporterMessagesEnabled = true,
                VisibleInOverview = true,
                SupporterType = SupporterType.User
            };

            await db.Supporters.AddAsync(supporterToAdd);

            await db.SaveChangesAsync();

            return supporterToAdd;
        }

        public async Task<IReadOnlyList<Supporter>> GetAllVisibleSupporters()
        {
            await using var db = this._contextFactory.CreateDbContext();

            return await db.Supporters
                .AsQueryable()
                .Where(w => w.VisibleInOverview)
                .OrderByDescending(o => o.SupporterType)
                .ToListAsync();
        }
    }
}
