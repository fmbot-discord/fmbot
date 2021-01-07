using System.Collections.Generic;
using System.Linq;
using Discord;
using FMBot.Bot.Extensions;
using FMBot.Bot.Models;
using FMBot.Domain;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Services.WhoKnows
{
    public class WhoKnowsService
    {
        public static IList<WhoKnowsObjectWithUser> AddOrReplaceUserToIndexList(IList<WhoKnowsObjectWithUser> users, GuildUser guildUser, string name, long? playcount)
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
            });

            return users.OrderByDescending(o => o.Playcount).ToList();
        }

        public static string WhoKnowsListToString(IList<WhoKnowsObjectWithUser> whoKnowsObjects, int requestedUserId, CrownModel crownModel = null)
        {
            var reply = "";

            var whoKnowsCount = whoKnowsObjects.Count;
            if (whoKnowsCount > 14)
            {
                whoKnowsCount = 14;
            }

            var usersToShow = whoKnowsObjects
                .OrderByDescending(o => o.Playcount)
                .Take(14)
                .ToList();

            var spacer = crownModel?.Crown == null ? "" : " ";

            for (var index = 0; index < whoKnowsCount; index++)
            {
                var user = usersToShow[index];

                var nameWithLink = NameWithLink(user);
                var playString = StringExtensions.GetPlaysString(user.Playcount);

                var positionCounter = $"{spacer}{index + 1}. ";

                if (crownModel?.Crown != null && crownModel.Crown.UserId == user.UserId)
                {
                    positionCounter = "👑 ";
                }

                reply += $"{positionCounter} {nameWithLink}";

                reply += $" - **{user.Playcount}** {playString}\n";
            }

            if (!usersToShow.Select(s => s.UserId).Contains(requestedUserId))
            {
                var requestedUser = whoKnowsObjects.FirstOrDefault(f => f.UserId == requestedUserId);
                if (requestedUser != null)
                {
                    var nameWithLink = NameWithLink(requestedUser);
                    var playString = StringExtensions.GetPlaysString(requestedUser.Playcount);

                    reply += $"{spacer}{whoKnowsObjects.IndexOf(requestedUser) + 1}.  {nameWithLink} ";

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
    }
}
