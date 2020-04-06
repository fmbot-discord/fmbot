using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using FMBot.Bot.Resources;
using FMBot.Data;
using FMBot.Domain.ApiModels;
using FMBot.Domain.BotModels;
using FMBot.Domain.DatabaseModels;
using Microsoft.EntityFrameworkCore;
using Artist = FMBot.Domain.DatabaseModels.Artist;

namespace FMBot.Bot.Services
{
    public class ArtistsService
    {
        private readonly FMBotDbContext _db = new FMBotDbContext();

        public IReadOnlyList<ArtistWithUser> AddArtistToIndexList(IReadOnlyList<ArtistWithUser> artists, User userSettings, IGuildUser user, ArtistResponse artist)
        {
            var newArtistList = artists.ToList();
            newArtistList.Add(new ArtistWithUser
            {
                UserId = userSettings.UserId,
                ArtistName = artist.Artist.Name,
                Playcount = Convert.ToInt32(artist.Artist.Stats.Userplaycount.Value),
                LastFMUsername = userSettings.UserNameLastFM,
                DiscordUserId = userSettings.DiscordUserId,
                DiscordName = user.Nickname ?? user.Username
            });

            return newArtistList;
        }

        public string ArtistWithUserToStringList(IReadOnlyList<ArtistWithUser> artists, ArtistResponse artistResponse, int userId)
        {
            var reply = "";
            for (var index = 0; index < artists.Count; index++)
            {
                var artist = artists[index];
                var discordName = artist.DiscordName.Replace("(", "").Replace(")", "").Replace("[", "").Replace("]", "");
                var playString = "plays";
                if (artist.Playcount == 1)
                {
                    playString = "play";
                }

                if (index == 0)
                {
                    reply += $"👑 [{discordName}]({Constants.LastFMUserUrl}{artist.LastFMUsername}) ";
                }
                else
                {
                    reply += $" {index + 1}.  [{discordName}]({Constants.LastFMUserUrl}{artist.LastFMUsername}) ";
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

        public async Task<IReadOnlyList<ArtistWithUser>> GetIndexedUsersForArtist(IReadOnlyCollection<IGuildUser> guildUsers, string artistName)
        {
            var userIds = guildUsers.Select(s => s.Id);

            var artists = await this._db.Artists
                .Include(i => i.User)
                .Where(w => w.Name.ToLower() == artistName.ToLower()
                            && userIds.Contains(w.User.DiscordUserId))
                .OrderByDescending(o => o.Playcount)
                .Take(15)
                .ToListAsync();

            return artists
                .Select(s =>
                {
                    var discordUser = guildUsers.First(f => f.Id == s.User.DiscordUserId);
                    return new ArtistWithUser
                    {
                        ArtistName = s.Name,
                        DiscordName = discordUser.Nickname ?? discordUser.Username,
                        Playcount = s.Playcount,
                        DiscordUserId = s.User.DiscordUserId,
                        LastFMUsername = s.User.UserNameLastFM,
                        UserId = s.UserId
                    };
                }).ToList();
        }


        public async Task<int> GetArtistListenerCountForServer(IReadOnlyCollection<IGuildUser> guildUsers, string artistName)
        {
            var userIds = guildUsers.Select(s => s.Id);

            return await this._db.Artists
                .Include(i => i.User)
                .Where(w => w.Name.ToLower() == artistName.ToLower()
                            && userIds.Contains(w.User.DiscordUserId))
                .CountAsync();
        }

        public async Task<int> GetArtistPlayCountForServer(IReadOnlyCollection<IGuildUser> guildUsers, string artistName)
        {
            var userIds = guildUsers.Select(s => s.Id);

            var query = this._db.Artists
                .Include(i => i.User)
                .Where(w => w.Name.ToLower() == artistName.ToLower()
                            && userIds.Contains(w.User.DiscordUserId));

            if (await query.AnyAsync())
            {
                return await query.SumAsync(s => s.Playcount);
            }

            return 0;
        }

        public async Task<double> GetArtistAverageListenerPlaycountForServer(IReadOnlyCollection<IGuildUser> guildUsers, string artistName)
        {
            var userIds = guildUsers.Select(s => s.Id);

            var query = this._db.Artists
                .Include(i => i.User)
                .Where(w => w.Name.ToLower() == artistName.ToLower()
                            && userIds.Contains(w.User.DiscordUserId));

            if (await query.AnyAsync())
            {
                return await query.AverageAsync(s => s.Playcount);
            }

            return 0;
        }
    }
}
