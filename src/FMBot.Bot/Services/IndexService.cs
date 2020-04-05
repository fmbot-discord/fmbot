using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord.Commands;
using FMBot.Bot.Configurations;
using FMBot.Bot.Extensions;
using FMBot.Bot.Resources;
using FMBot.Data;
using FMBot.Domain.BotModels;
using FMBot.Domain.DatabaseModels;
using IF.Lastfm.Core.Api;
using IF.Lastfm.Core.Api.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using PostgreSQLCopyHelper;

namespace FMBot.Bot.Services
{
    public class IndexService
    {
        private readonly LastfmClient _lastFMClient = new LastfmClient(ConfigData.Data.FMKey, ConfigData.Data.FMSecret);

        private readonly FMBotDbContext _db = new FMBotDbContext();

        private readonly IUserIndexQueue _userIndexQueue;

        public IndexService(IUserIndexQueue userIndexQueue)
        {
            this._userIndexQueue = userIndexQueue;
            this._userIndexQueue.UsersToIndex.SubscribeAsync(OnNextAsync);
        }

        private async Task OnNextAsync(User obj)
        {
            await StoreArtistsForUser(obj);
        }

        public async Task<int> IndexGuild(IReadOnlyList<User> users)
        {
            Console.WriteLine($"Starting artist update for {users.Count} users");

            this._userIndexQueue.Publish(users.ToList());

            var usersInQueue = await this._userIndexQueue.UsersToIndex.Count();

            return usersInQueue;
        }

        private async Task StoreArtistsForUser(User user)
        {
            Thread.Sleep(800);

            Console.WriteLine($"Starting artist store for {user.UserNameLastFM}");

            var topArtists = await this._lastFMClient.User.GetTopArtists(user.UserNameLastFM, LastStatsTimeSpan.Overall, 1, Constants.ArtistsToIndex);
            Statistics.LastfmApiCalls.Inc();

            var now = DateTime.UtcNow;
            var artists = topArtists.Select(a => new Artist
            {
                LastUpdated = now,
                Name = a.Name,
                Playcount = a.PlayCount.Value,
                UserId = user.UserId
            }).ToList();

            var connString = this._db.Database.GetDbConnection().ConnectionString;
            var copyHelper = new PostgreSQLCopyHelper<Artist>("public", "artists")
                .MapText("name", x => x.Name)
                .MapInteger("user_id", x => x.UserId)
                .MapInteger("playcount", x => x.Playcount)
                .MapTimeStamp("last_updated", x => x.LastUpdated);

            await using (var connection = new NpgsqlConnection(connString))
            {
                connection.Open();

                await using var deleteCurrentArtists = new NpgsqlCommand($"DELETE FROM public.artists WHERE user_id = {user.UserId};", connection);
                await deleteCurrentArtists.ExecuteNonQueryAsync();

                copyHelper.SaveAll(connection, artists);

                await using var setIndexTime = new NpgsqlCommand($"UPDATE public.users SET last_indexed='{now.ToString("u")}' WHERE user_id = {user.UserId};", connection);
                await setIndexTime.ExecuteNonQueryAsync();
            }
        }

        public async Task<IReadOnlyList<User>> GetUsersForContext(ICommandContext context)
        {
            var users = await context.Guild.GetUsersAsync();

            var userIds = users.Select(s => s.Id).ToList();

            var tooRecent = DateTime.UtcNow.Add(-Constants.GuildIndexCooldown);
            return await this._db.Users
                .Include(i => i.Artists)
                .Where(w => userIds.Contains(w.DiscordUserId)
                && w.LastIndexed == null || w.LastIndexed <= tooRecent)
                .ToListAsync();
        }
    }
}
