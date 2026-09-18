using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FMBot.Core;

public enum CensorResult
{
    Safe = 1,
    Nsfw = 2,
    NotSafe = 3
}

public class MusicCensorService
{
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly IMemoryCache _cache;
    private readonly BotSettings _botSettings;

    public MusicCensorService(IDbContextFactory<FMBotDbContext> contextFactory, IMemoryCache cache,
        IOptions<BotSettings> botSettings)
    {
        this._contextFactory = contextFactory;
        this._cache = cache;
        this._botSettings = botSettings.Value;
    }

    public async Task<List<CensoredMusic>> GetCachedCensoredMusic()
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

    public void ClearCache()
    {
        this._cache.Remove("censored-music");
    }

    public async Task<CensorResult> AlbumResult(string albumName, string artistName, bool featured = false)
    {
        var censoredMusic = await GetCachedCensoredMusic();

        var censoredArtist = censoredMusic
            .Where(w => w.Artist)
            .FirstOrDefault(f => string.Equals(f.ArtistName, artistName, StringComparison.OrdinalIgnoreCase));
        if (censoredArtist != null)
        {
            await IncreaseCensoredCount(censoredArtist.CensoredMusicId);

            if (censoredArtist.CensorType.HasFlag(CensorType.ArtistAlbumsCensored))
            {
                return CensorResult.NotSafe;
            }
            if (censoredArtist.CensorType.HasFlag(CensorType.ArtistAlbumsNsfw))
            {
                return CensorResult.Nsfw;
            }
            if (featured && censoredArtist.CensorType.HasFlag(CensorType.ArtistFeaturedBan))
            {
                return CensorResult.NotSafe;
            }
        }

        if (albumName != null)
        {
            if (censoredMusic
                .Select(s => s.ArtistName)
                .Contains(artistName, StringComparer.OrdinalIgnoreCase))
            {
                var album = censoredMusic
                    .Where(w => !w.Artist && w.AlbumName != null)
                    .FirstOrDefault(f => string.Equals(f.ArtistName, artistName, StringComparison.OrdinalIgnoreCase) &&
                                         string.Equals(f.AlbumName, albumName, StringComparison.OrdinalIgnoreCase));

                if (album != null)
                {
                    await IncreaseCensoredCount(album.CensoredMusicId);
                    if (album.CensorType.HasFlag(CensorType.AlbumCoverCensored))
                    {
                        return CensorResult.NotSafe;
                    }
                    if (album.CensorType.HasFlag(CensorType.AlbumCoverNsfw))
                    {
                        return CensorResult.Nsfw;
                    }
                }
            }
        }

        return CensorResult.Safe;
    }

    public async Task<CensorResult> ArtistResult(string artistName)
    {
        var censoredMusic = await GetCachedCensoredMusic();

        var censoredArtist = censoredMusic
            .Where(w => w.Artist)
            .FirstOrDefault(f => string.Equals(f.ArtistName, artistName, StringComparison.OrdinalIgnoreCase));

        if (censoredArtist != null)
        {
            await IncreaseCensoredCount(censoredArtist.CensoredMusicId);
            if (censoredArtist.CensorType.HasFlag(CensorType.ArtistImageCensored))
            {
                return CensorResult.NotSafe;
            }
            if (censoredArtist.CensorType.HasFlag(CensorType.ArtistImageNsfw))
            {
                return CensorResult.Nsfw;
            }
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
}
