using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord.Commands;
using FMBot.Data;
using FMBot.Data.Entities;
using FMBot.LastFM.Models;
using Microsoft.EntityFrameworkCore;
using Artist = FMBot.Data.Entities.Artist;

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
                if (index == 0)
                {
                    reply += $"👑 <@{artist.User.DiscordUserId}>";
                }
                else
                {
                    reply += $" {index + 1}.  <@{artist.User.DiscordUserId}> ";
                }
                if (artist.UserId != userId)
                {
                    reply += $"- **{artist.Playcount}** plays\n";
                }
                else
                {
                    reply += $"- **{artistResponse.Artist.Stats.Userplaycount}** plays\n";
                }
            }

            if (artists.Count == 1)
            {
                reply += "\nNobody else has this artist in their top 1000 artists.";
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
