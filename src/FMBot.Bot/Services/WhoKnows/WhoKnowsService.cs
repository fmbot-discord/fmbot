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
        public static IList<WhoKnowsObjectWithUser> AddOrReplaceUserToIndexList(IList<WhoKnowsObjectWithUser> users, User userSettings, IGuildUser user, string name, long? playcount)
        {
            var userRemoved = false;
            var existingRecord = users.FirstOrDefault(f => f.UserId == userSettings.UserId);
            if (existingRecord != null)
            {
                users.Remove(existingRecord);
                userRemoved = true;
            }

            var userPlaycount = int.Parse(playcount.GetValueOrDefault(0).ToString());
            users.Add(new WhoKnowsObjectWithUser
            {
                UserId = userSettings.UserId,
                Name = name,
                Playcount = userPlaycount,
                LastFMUsername = userSettings.UserNameLastFM,
                DiscordUserId = userSettings.DiscordUserId,
                DiscordName = user.Nickname ?? user.Username,
                NoPosition = !userRemoved
            });

            return users.OrderByDescending(o => o.Playcount).ToList();
        }

        public static string WhoKnowsListToString(IList<WhoKnowsObjectWithUser> whoKnowsObjects)
        {
            var reply = "";

            var artistsCount = whoKnowsObjects.Count;
            if (artistsCount > 14)
            {
                artistsCount = 14;
            }

            for (var index = 0; index < artistsCount; index++)
            {
                var user = whoKnowsObjects[index];

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
            }

            var userWithNoPosition = whoKnowsObjects.FirstOrDefault(f => f.NoPosition);
            if (userWithNoPosition != null)
            {
                var nameWithLink = NameWithLink(userWithNoPosition);
                var playString = StringExtensions.GetPlaysString(userWithNoPosition.Playcount);

                reply += $"  ...   {nameWithLink} ";
                reply += $"- **{userWithNoPosition.Playcount}** {playString}\n";
            }

            return reply;
        }

        private static string NameWithLink(WhoKnowsObjectWithUser user)
        {
            var discordName = user.DiscordName;
            var charsToRemove = new[] { "@", "[", "]", "(", ")","`","|","*","~",">"};

            foreach (var c in charsToRemove)
            {
                discordName = discordName.Replace(c, string.Empty);
            }

            if (string.IsNullOrWhiteSpace(discordName))
            {
                discordName = user.LastFMUsername;
            }

            var nameWithLink = $"[{discordName}]({Constants.LastFMUserUrl}{user.LastFMUsername})";
            return nameWithLink;
        }
    }
}
