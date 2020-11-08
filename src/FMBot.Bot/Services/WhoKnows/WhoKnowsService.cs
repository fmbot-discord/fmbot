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
            var userRemoved = false;
            var existingUsers = users
                .Where(f => f.LastFMUsername.ToLower() == guildUser.User.UserNameLastFM.ToLower());
            if (existingUsers.Any())
            {
                users = users
                    .Where(f => f.LastFMUsername.ToLower() != guildUser.User.UserNameLastFM.ToLower())
                    .ToList();
                userRemoved = true;
            }

            var userPlaycount = int.Parse(playcount.GetValueOrDefault(0).ToString());
            users.Add(new WhoKnowsObjectWithUser
            {
                UserId = guildUser.User.UserId,
                Name = name,
                Playcount = userPlaycount,
                LastFMUsername = guildUser.User.UserNameLastFM,
                DiscordName = guildUser.UserName,
                NoPosition = !userRemoved
            });

            return users.OrderByDescending(o => o.Playcount).ToList();
        }

        public static string WhoKnowsListToString(IList<WhoKnowsObjectWithUser> whoKnowsObjects)
        {
            var reply = "";

            var usersWithPositions = whoKnowsObjects
                .Where(w => !w.NoPosition)
                .ToList();

            var artistsCount = usersWithPositions.Count;
            if (artistsCount > 14)
            {
                artistsCount = 14;
            }

            var position = 0;
            for (var index = 0; index < artistsCount; index++)
            {
                var user = usersWithPositions[index];

                var nameWithLink = NameWithLink(user);
                var playString = StringExtensions.GetPlaysString(user.Playcount);

                if (index == 0)
                {
                    reply += $"👑  {nameWithLink}";
                }
                else
                {
                    reply += $" {index + 1}.  {nameWithLink} ";
                }

                reply += $"- **{user.Playcount}** {playString}\n";
                position++;
            }

            var userWithNoPosition = whoKnowsObjects.FirstOrDefault(f => f.NoPosition);
            if (userWithNoPosition != null)
            {
                var nameWithLink = NameWithLink(userWithNoPosition);
                var playString = StringExtensions.GetPlaysString(userWithNoPosition.Playcount);

                if (position < 14)
                {
                    reply += $"  {position + 1}.  {nameWithLink} ";
                }
                else
                {
                    reply += $"  ...   {nameWithLink} ";
                }
                reply += $"- **{userWithNoPosition.Playcount}** {playString}\n";
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
