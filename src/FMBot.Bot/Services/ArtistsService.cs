using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord.Commands;
using FMBot.Data;
using FMBot.LastFM.Models;
using Microsoft.EntityFrameworkCore;
using Artist = FMBot.Data.Entities.Artist;

namespace FMBot.Bot.Services
{
    public class ArtistsService
    {
        private readonly FMBotDbContext _db = new FMBotDbContext();

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
