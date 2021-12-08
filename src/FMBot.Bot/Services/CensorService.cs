using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FMBot.Bot.Services
{
    public class CensorService
    {
        private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
        private readonly IMemoryCache _cache;
        private readonly BotSettings _botSettings;

        public CensorService(IDbContextFactory<FMBotDbContext> contextFactory, IMemoryCache cache, IOptions<BotSettings> botSettings)
        {
            this._contextFactory = contextFactory;
            this._cache = cache;
            this._botSettings = botSettings.Value;
        }

        public record CensorResult(bool Result, string AlternativeCover = null);

        public async Task<CensorResult> IsSafeForChannel(ICommandContext context, string albumName, string artistName, string url, EmbedBuilder embed = null)
        {
            if (!await AlbumIsSafe(albumName, artistName))
            {
                var allowedInNsfw = await AlbumIsAllowedInNsfw(albumName, artistName);
                if (!allowedInNsfw.Result)
                {
                    embed?.WithDescription("Sorry, this album or artist can't be posted due to discord ToS.\n" +
                                           $"You can view the [album cover here]({url}).");
                    return new CensorResult(false);
                }
                if (context.Guild != null && !((SocketTextChannel)context.Channel).IsNsfw)
                {
                    embed?.WithDescription("Sorry, this album cover can only be posted in NSFW channels.\n" +
                                                $"You can mark this channel as NSFW or view the [album cover here]({url}).");
                    return new CensorResult(false);
                }

                return new CensorResult(true, allowedInNsfw.AlternativeCover);
            }

            return new CensorResult(true);
        }

        public async Task<bool> AlbumIsSafe(string albumName, string artistName)
        {
            var censoredMusic = await GetCachedCensoredMusic();

            if (censoredMusic
                    .Where(w => w.Artist)
                    .Select(s => s.ArtistName.ToLower())
                    .Contains(artistName.ToLower()))
            {
                return false;
            }

            if (albumName == null)
            {
                return true;
            }

            if (censoredMusic
                    .Select(s => s.ArtistName.ToLower())
                    .Contains(artistName.ToLower()))
            {
                var censoredAlbum = censoredMusic
                    .Where(w => !w.Artist && w.ArtistName.ToLower() == artistName.ToLower() && w.AlbumName != null)
                    .FirstOrDefault(f => f.AlbumName.ToLower() == albumName.ToLower());

                if (censoredAlbum != null)
                {
                    await IncreaseCensoredCount(censoredAlbum.CensoredMusicId);
                    return false;
                }
            }

            return true;
        }

        private async Task<List<CensoredMusic>> GetCachedCensoredMusic()
        {
            const string cacheKey = "censored-music";
            var cacheTime = TimeSpan.FromMinutes(5);

            if (this._cache.TryGetValue(cacheKey, out List<CensoredMusic> cachedCensoredMusic))
            {
                return cachedCensoredMusic;
            }

            await using var db = this._contextFactory.CreateDbContext();
            var censoredMusic = await db.CensoredMusic
                .AsQueryable()
                .ToListAsync();

            this._cache.Set(cacheKey, censoredMusic, cacheTime);

            return censoredMusic;
        }

        private void ClearCensoredCache()
        {
            this._cache.Remove("censored-music");
        }

        public async Task<CensorResult> AlbumIsAllowedInNsfw(string albumName, string artistName)
        {
            var censoredMusic = await GetCachedCensoredMusic();

            var artistAllowedInNSfw = censoredMusic
                .Where(w => w.Artist && w.SafeForCommands)
                .FirstOrDefault(f => f.ArtistName.ToLower() == artistName.ToLower());
            if (artistAllowedInNSfw != null)
            {
                await IncreaseCensoredCount(artistAllowedInNSfw.CensoredMusicId);
                return new CensorResult(true);
            }

            if (censoredMusic
                    .Where(w => w.SafeForCommands)
                    .Select(s => s.ArtistName.ToLower())
                    .Contains(artistName.ToLower()))
            {
                var album = censoredMusic
                    .Where(w => !w.Artist && w.AlbumName != null && w.SafeForCommands)
                    .FirstOrDefault(f => f.ArtistName.ToLower() == artistName.ToLower() &&
                                              f.AlbumName.ToLower() == albumName.ToLower());

                if (album != null)
                {
                    await IncreaseCensoredCount(album.CensoredMusicId);
                    return new CensorResult(true, album.AlternativeCoverUrl);
                }
            }

            return new CensorResult(false);
        }

        private async Task IncreaseCensoredCount(int censoredMusicId)
        {
            await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
            await connection.OpenAsync();

            const string sql = "UPDATE censored_music SET times_censored = COALESCE(times_censored, 0) + 1 WHERE censored_music_id = @censoredMusicId;";
            DefaultTypeMap.MatchNamesWithUnderscores = true;

            await connection.QueryAsync(sql, new
            {
                censoredMusicId
            });
            await connection.CloseAsync();
        }

        public async Task AddCensoredAlbum(string albumName, string artistName)
        {
            ClearCensoredCache();
            await using var db = this._contextFactory.CreateDbContext();

            await db.CensoredMusic.AddAsync(new CensoredMusic
            {
                AlbumName = albumName,
                ArtistName = artistName,
                Artist = false,
                SafeForCommands = false,
                SafeForFeatured = false
            });

            await db.SaveChangesAsync();
        }

        public async Task AddNsfwAlbum(string albumName, string artistName)
        {
            ClearCensoredCache();
            await using var db = this._contextFactory.CreateDbContext();

            await db.CensoredMusic.AddAsync(new CensoredMusic
            {
                AlbumName = albumName,
                ArtistName = artistName,
                Artist = false,
                SafeForCommands = true,
                SafeForFeatured = false
            });

            await db.SaveChangesAsync();
        }

        public async Task AddCensoredArtist(string artistName)
        {
            ClearCensoredCache();
            await using var db = this._contextFactory.CreateDbContext();

            await db.CensoredMusic.AddAsync(new CensoredMusic
            {
                ArtistName = artistName,
                Artist = true,
                SafeForCommands = false,
                SafeForFeatured = false
            });

            await db.SaveChangesAsync();
        }
    }
}
