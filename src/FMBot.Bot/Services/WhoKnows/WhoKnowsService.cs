using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Domain;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;

namespace FMBot.Bot.Services.WhoKnows
{
    public class WhoKnowsService
    {
        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;

        public WhoKnowsService(IDbContextFactory<FMBotDbContext> contextFactory)
        {
            this._contextFactory = contextFactory;
        }


        public static IList<WhoKnowsObjectWithUser> AddOrReplaceUserToIndexList(ICollection<WhoKnowsObjectWithUser> users, GuildUser guildUser, string name, long? playcount)
        {
            var existingUsers = users
                .Where(f => f.LastFMUsername.ToLower() == guildUser.User.UserNameLastFM.ToLower());
            if (existingUsers.Any())
            {
                users = users
                    .Where(f => f.LastFMUsername.ToLower() != guildUser.User.UserNameLastFM.ToLower())
                    .ToList();
            }

            var userPlaycount = int.Parse(playcount.GetValueOrDefault(0).ToString());
            users.Add(new WhoKnowsObjectWithUser
            {
                UserId = guildUser.User.UserId,
                Name = name,
                Playcount = userPlaycount,
                LastFMUsername = guildUser.User.UserNameLastFM,
                DiscordName = guildUser.UserName,
                PrivacyLevel = PrivacyLevel.Global
            });

            return users.OrderByDescending(o => o.Playcount).ToList();
        }

        public static IList<WhoKnowsObjectWithUser> FilterGuildUsersAsync(ICollection<WhoKnowsObjectWithUser> users, Persistence.Domain.Models.Guild guild)
        {
            if (guild.ActivityThresholdDays.HasValue)
            {
                var usersToFilter = guild.GuildUsers.Where(w =>
                        w.User.LastUsed == null ||
                        w.User.LastUsed < DateTime.UtcNow.AddDays(-guild.ActivityThresholdDays.Value))
                    .ToList();

                users = users
                    .Where(w => !usersToFilter.Select(s => s.UserId).Contains(w.UserId))
                    .ToList();
            }
            if (guild.GuildBlockedUsers != null && guild.GuildBlockedUsers.Any(a => a.BlockedFromWhoKnows))
            {
                var usersToFilter = guild.GuildBlockedUsers
                    .Where(w => w.BlockedFromWhoKnows)
                    .ToList();

                users = users
                    .Where(w => !usersToFilter.Select(s => s.UserId).Contains(w.UserId))
                    .ToList();
            }

            return users.ToList();
        }

        public async Task<IList<WhoKnowsObjectWithUser>> FilterGlobalUsersAsync(ICollection<WhoKnowsObjectWithUser> users)
        {
            await using var db = this._contextFactory.CreateDbContext();
            var bottedUsers = await db.BottedUsers
                .AsQueryable()
                .ToListAsync();

            return users
                .Where(w =>
                    !bottedUsers
                    .Select(s => s.UserNameLastFM.ToLower())
                    .Contains(w.LastFMUsername.ToLower()))
                .ToList();
        }

        public static IList<WhoKnowsObjectWithUser> ShowGuildMembersInGlobalWhoKnowsAsync(IList<WhoKnowsObjectWithUser> users, IList<GuildUser> guildUsers)
        {
            foreach (var user in users.Where(w => guildUsers.Select(s => s.UserId).Contains(w.UserId)))
            {
                user.PrivacyLevel = PrivacyLevel.Global;
            }

            return users;
        }

        public static string WhoKnowsListToString(IList<WhoKnowsObjectWithUser> whoKnowsObjects, int requestedUserId,
            PrivacyLevel minPrivacyLevel, CrownModel crownModel = null, bool hidePrivateUsers = false)
        {
            var reply = "";

            var whoKnowsCount = whoKnowsObjects.Count;
            if (whoKnowsCount > 14)
            {
                whoKnowsCount = 14;
            }

            var usersToShow = whoKnowsObjects
                .OrderByDescending(o => o.Playcount)
                .ToList();

            var spacer = crownModel?.Crown == null ? "" : " ";

            var indexNumber = 1;
            var timesNameAdded = 0;
            var requestedUserAdded = false;


            // Note: You might not be able to see them, but this code contains specific spacers
            // https://www.compart.com/en/unicode/category/Zs
            for (var index = 0; timesNameAdded < whoKnowsCount; index++)
            {
                if (index >= usersToShow.Count)
                {
                    break;
                }

                var user = usersToShow[index];

                string nameWithLink;
                if (minPrivacyLevel == PrivacyLevel.Global && user.PrivacyLevel != PrivacyLevel.Global)
                {
                    nameWithLink = PrivateName();
                    if (hidePrivateUsers)
                    {
                        indexNumber += 1;
                        continue;
                    }
                }
                else
                {
                    nameWithLink = NameWithLink(user);
                }

                var playString = StringExtensions.GetPlaysString(user.Playcount);

                var positionCounter = $"{spacer}{indexNumber}.";
                positionCounter = user.UserId == requestedUserId ? $"**{positionCounter}** " : $"{positionCounter} ";

                if (crownModel?.Crown != null && crownModel.Crown.UserId == user.UserId)
                {
                    positionCounter = "👑 ";
                }

                var afterPositionSpacer = index + 1 == 10 ? "" : " ";

                reply += $"{positionCounter}{afterPositionSpacer}{nameWithLink}";

                reply += $" - **{user.Playcount}** {playString}\n";

                indexNumber += 1;
                timesNameAdded += 1;

                if (user.UserId == requestedUserId)
                {
                    requestedUserAdded = true;
                }
            }

            if (!requestedUserAdded)
            {
                var requestedUser = whoKnowsObjects.FirstOrDefault(f => f.UserId == requestedUserId);
                if (requestedUser != null)
                {
                    var nameWithLink = NameWithLink(requestedUser);
                    var playString = StringExtensions.GetPlaysString(requestedUser.Playcount);

                    reply += $"**{spacer}{whoKnowsObjects.IndexOf(requestedUser) + 1}.**  {nameWithLink} ";

                    reply += $" - **{requestedUser.Playcount}** {playString}\n";
                }
            }

            if (crownModel?.CrownResult != null)
            {
                reply += $"\n{crownModel.CrownResult}";
            }

            return reply;
        }

        private static string NameWithLink(WhoKnowsObjectWithUser user)
        {
            var discordName = Format.Sanitize(user.DiscordName);

            if (string.IsNullOrWhiteSpace(discordName))
            {
                discordName = user.LastFMUsername;
            }

            var nameWithLink = $"[{discordName}]({Constants.LastFMUserUrl}{user.LastFMUsername})";
            return nameWithLink;
        }

        private static string PrivateName()
        {
            return "Private user";
        }
    }
}
