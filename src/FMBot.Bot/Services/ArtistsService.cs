using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord.Commands;
using FMBot.Bot.Resources;
using FMBot.Data;
using FMBot.Domain.ApiModels;
using FMBot.Domain.DatabaseModels;
using Microsoft.EntityFrameworkCore;
using Artist = FMBot.Domain.DatabaseModels.Artist;

namespace FMBot.Bot.Services
{
    public class ArtistsService
    {
        private readonly FMBotDbContext _db = new FMBotDbContext();

        public IReadOnlyList<Artist> AddArtistToIndexList(IReadOnlyList<Artist> artists, User userSettings, ArtistResponse artist)
        {
            var newArtistList = artists.ToList();
            newArtistList.Add(new Artist
            {
                UserId = userSettings.UserId,
                Name = artist.Artist.Name,
                Playcount = Convert.ToInt32(artist.Artist.Stats.Userplaycount.Value),
                User = new User
                {
                    DiscordUserId = userSettings.DiscordUserId
                }
            });

            return newArtistList;
        }

        public async Task<string> UserListToIndex(IReadOnlyList<Artist> artists, ArtistResponse artistResponse, int userId)
        {
            var reply = "";
            for (var index = 0; index < artists.Count; index++)
            {
                var artist = artists[index];
                var playString = "plays";
                if (artist.Playcount == 1)
                {
                    playString = "play";
                }

                if (index == 0)
                {
                    reply += $"👑 [{artist.User.UserNameLastFM}]({Constants.LastFMUserUrl}{artist.User.UserNameLastFM}) ";
                }
                else
                {
                    reply += $" {index + 1}.  [{artist.User.UserNameLastFM}]({Constants.LastFMUserUrl}{artist.User.UserNameLastFM}) ";
                }
                if (artist.UserId != userId)
                {
                    reply += $"- **{artist.Playcount}** {playString}\n";
                }
                else
                {
                    reply += $"- **{artistResponse.Artist.Stats.Userplaycount}** {playString}\n";
                }
            }

            if (artists.Count == 1)
            {
                reply += $"\nNobody else has this artist in their top {Constants.ArtistsToIndex} artists.";
            }

            return reply;
        }

        public async Task<IReadOnlyList<Artist>> GetUsersForArtist(ICommandContext context, string artistName)
        {
            var users = await context.Guild.GetUsersAsync();

            var userIds = users.Select(s => s.Id).ToList();

            return await this._db.Artists
                .Include(i => i.User)
                .Where(w => w.Name == artistName
                            && userIds.Contains(w.User.DiscordUserId))
                .OrderByDescending(o => o.Playcount)
                .Take(15)
                .ToListAsync();
        }
    }
}
