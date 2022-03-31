using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Discord;
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

        private readonly List<PrivateCover> _privateCovers;

        public CensorService(IDbContextFactory<FMBotDbContext> contextFactory, IMemoryCache cache, IOptions<BotSettings> botSettings)
        {
            this._contextFactory = contextFactory;
            this._cache = cache;
            this._botSettings = botSettings.Value;

            this._privateCovers = new List<PrivateCover>
            {
                new("Eminem", "The Marshall Mathers LP",
                    "https://cdn.discordapp.com/attachments/956641038850732052/956641456087527424/unknown.png",
                    "Frikandel"),
                new("Ecco2k", "E",
                    "https://cdn.discordapp.com/attachments/956641038850732052/956641740541009950/ecco2k.png",
                    "Voaz"),
                new("Weezer", "Weezer",
                    "https://cdn.discordapp.com/attachments/956641038850732052/956641867095756850/weezer.png",
                    "twojett"),
                new("Nirvana", "Nevermind (Remastered)",
                    "https://i.imgur.com/OMwf8v2.png",
                    "manlethamlet"),
                new("Nirvana", "Nevermind",
                    "https://i.imgur.com/OMwf8v2.png",
                    "manlethamlet"),
                new("Tyler, the Creator", "IGOR",
                    "https://cdn.discordapp.com/attachments/956641038850732052/956642020338856026/fmbotigor.png",
                    "Pingu"),
                new("Death Grips", "No Love Deep Web",
                    "https://cdn.discordapp.com/attachments/956641038850732052/956642823648710696/no_love_deep_web.png",
                    "blinksu"),
                new("The Weeknd", "Dawn FM",
                    "https://cdn.discordapp.com/attachments/956641038850732052/956643204676063332/unknown.png",
                    "Cajmo"),
                new("Joji", "Ballads 1",
                    "https://cdn.discordapp.com/attachments/956641038850732052/956643755472081017/Joji_-_Ballads_1.jpg",
                    "Winterbay"),
                new("Radiohead", "OK Computer",
                    "https://cdn.discordapp.com/attachments/956641038850732052/956655681765769216/IMG_6592.png",
                    "firefly"),
                new("Gorillaz", "Plastic Beach",
                    "https://cdn.discordapp.com/attachments/956641038850732052/956658895433134130/IMG_1966.png",
                    "ori with a gun"),
                new("Kanye West", "My Beautiful Dark Twisted Fantasy",
                    "https://cdn.discordapp.com/attachments/930575892730773555/959154697099444254/MBDTF-alt.png",
                    "ky"),
                new("Lorde", "Solar Power",
                    "https://cdn.discordapp.com/attachments/956641038850732052/956664140057956464/Screen_Shot_2022-03-24_at_2.49.54_PM.jpg",
                    "arap"),
                new("Baby Keem", "The Melodic Blue",
                    "https://cdn.discordapp.com/attachments/956641038850732052/956665642885468240/unknown.png",
                    "Kefkefk123"),
                new("Tame Impala", "Currents",
                    "https://cdn.discordapp.com/attachments/956641038850732052/956667532771729478/unknown.png",
                    "Fincy"),
                new("bladee", "Crest",
                    "https://cdn.discordapp.com/attachments/956641038850732052/957363629567660074/bladee.png",
                    "kvltkvm"),
                new("Kanye West", "The Life of Pablo",
                    "https://cdn.discordapp.com/attachments/956641038850732052/957406020597080094/tlop-alt.png",
                    "peeno24"),
                new("Playboi Carti", "Whole Lotta Red",
                    "https://cdn.discordapp.com/attachments/956641038850732052/957480967465992222/wlr.png",
                    "Digestive520"),
                new("Mitski", "Be the Cowboy",
                    "https://cdn.discordapp.com/attachments/956641038850732052/957523399251464253/even_better_masterpiece.png",
                    "starifshes"),
                new("Travis Scott", "ASTROWORLD",
                    "https://cdn.discordapp.com/attachments/956641038850732052/958273608298418186/a.jpg",
                    "Lingered"),
                new("Death Grips", "The Money Store",
                    "https://cdn.discordapp.com/attachments/956641038850732052/958278631875031070/the_money_store.png",
                    "Lyren"),
                new("C418", "Minecraft - Volume Alpha",
                    "https://cdn.discordapp.com/attachments/956641038850732052/958292660903358464/unknown.png",
                    "Regor"),
                new("Frank Ocean", "Blonde",
                    "https://cdn.discordapp.com/attachments/956641038850732052/958301501405687828/square-1471882460-frank-ocean-blonde.png",
                    "Subwayy301"),
                new("Mac DeMarco", "This Old Dog",
                    "https://cdn.discordapp.com/attachments/956641038850732052/958378118492618834/IMG_6796.png",
                    "blackholefriend"),
                new("Bladee", "ICEDANCER",
                    "https://cdn.discordapp.com/attachments/956641038850732052/958456603345047642/0B66141C-64B7-4916-A36A-4551A171FB2F.jpg",
                    "drasil"),
                new("Kendrick Lamar", "DAMN.",
                    "https://media.discordapp.net/attachments/764700402868158464/958520925639295036/Kendrick-Lamar-Damn-album-cover-820.png",
                    "Subwayy301"),
            };
        }

        public record CensorResult(bool Result, string AlternativeCover = null, PrivateCover PrivateCover = null, string privateCoverText = null);
        public record PrivateCover(string ArtistName, string AlbumName, string Url, string Source);

        public async Task<CensorResult> IsSafeForChannel(IGuild guild, IChannel channel, string albumName, string artistName, string url, EmbedBuilder embed = null, bool usePrivateCover = false)
        {
            if (usePrivateCover && albumName != null && DateTime.UtcNow <= new DateTime(2022, 4, 1))
            {
                var privateCover = this._privateCovers.FirstOrDefault(f =>
                    f.AlbumName.ToLower() == albumName.ToLower() &&
                    f.ArtistName.ToLower() == artistName.ToLower());

                if (privateCover != null)
                {
                    return new CensorResult(true, privateCover.Url, privateCover, $"\nAdvanced Copyright Avoidance System Activated! Source: {privateCover.Source}");
                }
            }

            if (!await AlbumIsSafe(albumName, artistName))
            {
                var allowedInNsfw = await AlbumIsAllowedInNsfw(albumName, artistName);
                if (!allowedInNsfw.Result)
                {
                    embed?.WithDescription("Sorry, this album or artist can't be posted due to discord ToS.\n" +
                                           $"You can view the [album cover here]({url}).");
                    return new CensorResult(false, allowedInNsfw.AlternativeCover);
                }
                if (guild != null && !((SocketTextChannel)channel).IsNsfw)
                {
                    embed?.WithDescription("Sorry, this album cover can only be posted in NSFW channels.\n" +
                                                $"You can mark this channel as NSFW or view the [album cover here]({url}).");
                    return new CensorResult(false, allowedInNsfw.AlternativeCover);
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
