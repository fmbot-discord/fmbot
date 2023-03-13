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

namespace FMBot.Bot.Services;

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

    public enum CensorResult
    {
        Safe = 1,
        Nsfw = 2,
        NotSafe = 3
    }

    public async Task<CensorResult> IsSafeForChannel(IGuild guild, IChannel channel, string albumName, string artistName, string url, EmbedBuilder embedToUpdate = null)
    {
        var result = await AlbumResult(albumName, artistName);
        if (result == CensorResult.NotSafe)
        {
            embedToUpdate?.WithDescription("Sorry, this album or artist can't be posted due to it possibly violating Discord ToS.\n" +
                                   $"You can view the [album cover here]({url}).");
            return result;
        }

        if (result == CensorResult.Nsfw && (guild == null || ((SocketTextChannel)channel).IsNsfw))
        {
            return CensorResult.Safe;
        }

        return result;
    }

    public async Task<CensorResult> IsSafeForChannel(IGuild guild, IChannel channel, string artistName, string url, EmbedBuilder embedToUpdate = null)
    {
        var result = await ArtistResult(artistName);
        if (result == CensorResult.NotSafe)
        {
            embedToUpdate?.WithDescription("Sorry, this artist can't be posted due to it possibly violating Discord ToS.\n" +
                                           $"You can view the [album cover here]({url}).");
            return result;
        }

        if (result == CensorResult.Nsfw && (guild == null || ((SocketTextChannel)channel).IsNsfw))
        {
            return CensorResult.Safe;
        }

        return result;
    }

    private async Task<List<CensoredMusic>> GetCachedCensoredMusic()
    {
        const string cacheKey = "censored-music";
        var cacheTime = TimeSpan.FromMinutes(5);

        if (this._cache.TryGetValue(cacheKey, out List<CensoredMusic> cachedCensoredMusic))
        {
            return cachedCensoredMusic;
        }

        await using var db = await this._contextFactory.CreateDbContextAsync();
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

    public async Task<CensorResult> AlbumResult(string albumName, string artistName, bool featured = false)
    {
        var censoredMusic = await GetCachedCensoredMusic();

        if (!featured)
        {
            censoredMusic = censoredMusic
                .Where(w => w.FeaturedBanOnly != true || w.CensorType.HasFlag(CensorType.ArtistFeaturedBan))
                .ToList();
        }

        var censoredArtist = censoredMusic
            .Where(w => w.Artist)
            .FirstOrDefault(f => f.ArtistName.ToLower() == artistName.ToLower());
        if (censoredArtist != null)
        {
            await IncreaseCensoredCount(censoredArtist.CensoredMusicId);
            return censoredArtist.SafeForCommands ? CensorResult.Nsfw : CensorResult.NotSafe;
        }

        if (censoredMusic
            .Select(s => s.ArtistName.ToLower())
            .Contains(artistName.ToLower()))
        {
            var album = censoredMusic
                .Where(w => !w.Artist && w.AlbumName != null)
                .FirstOrDefault(f => f.ArtistName.ToLower() == artistName.ToLower() &&
                                     f.AlbumName.ToLower() == albumName.ToLower());

            if (album != null)
            {
                await IncreaseCensoredCount(album.CensoredMusicId);
                return album.SafeForCommands ? CensorResult.Nsfw : CensorResult.NotSafe;
            }
        }

        return CensorResult.Safe;
    }

    public async Task<CensorResult> ArtistResult(string artistName)
    {
        var censoredMusic = await GetCachedCensoredMusic();
        
        var censoredArtist = censoredMusic
            .Where(w => w.Artist && (w.CensorType.HasFlag(CensorType.ArtistImageNsfw) || w.CensorType.HasFlag(CensorType.ArtistImageCensored)))
            .FirstOrDefault(f => f.ArtistName.ToLower() == artistName.ToLower());

        if (censoredArtist != null)
        {
            await IncreaseCensoredCount(censoredArtist.CensoredMusicId);
            return censoredArtist.SafeForCommands ? CensorResult.Nsfw : CensorResult.NotSafe;
        }

        return CensorResult.Safe;
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

    public async Task<CensoredMusic> GetCurrentAlbum(string albumName, string artistName)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        return await db.CensoredMusic
            .OrderByDescending(o => o.TimesCensored)
            .FirstOrDefaultAsync(f => f.AlbumName.ToLower() == albumName.ToLower() &&
                                      f.ArtistName.ToLower() == artistName.ToLower());
    }

    public async Task<CensoredMusic> GetCurrentArtist(string artistName)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        return await db.CensoredMusic
            .OrderByDescending(o => o.TimesCensored)
            .FirstOrDefaultAsync(f => f.ArtistName.ToLower() == artistName.ToLower() && f.Artist);
    }

    public async Task<CensoredMusic> GetForId(int id)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        return await db.CensoredMusic
            .FirstOrDefaultAsync(f => f.CensoredMusicId == id);
    }

    public async Task<CensoredMusic> SetCensorType(CensoredMusic musicToUpdate, CensorType censorType)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var music = await db.CensoredMusic.FirstAsync(f => f.CensoredMusicId == musicToUpdate.CensoredMusicId);

        music.CensorType = censorType;

        db.Update(music);

        await db.SaveChangesAsync();

        return music;
    }

    public async Task AddCensoredAlbum(string albumName, string artistName)
    {
        ClearCensoredCache();
        await using var db = await this._contextFactory.CreateDbContextAsync();

        await db.CensoredMusic.AddAsync(new CensoredMusic
        {
            AlbumName = albumName,
            ArtistName = artistName,
            Artist = false,
            SafeForCommands = false,
            SafeForFeatured = false,
            CensorType = CensorType.AlbumCoverCensored
        });

        await db.SaveChangesAsync();
    }

    public async Task AddNsfwAlbum(string albumName, string artistName)
    {
        ClearCensoredCache();
        await using var db = await this._contextFactory.CreateDbContextAsync();

        await db.CensoredMusic.AddAsync(new CensoredMusic
        {
            AlbumName = albumName,
            ArtistName = artistName,
            Artist = false,
            SafeForCommands = true,
            SafeForFeatured = false,
            CensorType = CensorType.AlbumCoverNsfw
        });

        await db.SaveChangesAsync();
    }

    public async Task AddCensoredArtistAlbums(string artistName)
    {
        ClearCensoredCache();
        await using var db = await this._contextFactory.CreateDbContextAsync();

        await db.CensoredMusic.AddAsync(new CensoredMusic
        {
            ArtistName = artistName,
            Artist = true,
            SafeForCommands = false,
            SafeForFeatured = false,
            CensorType = CensorType.ArtistAlbumsCensored
        });

        await db.SaveChangesAsync();
    }
}
