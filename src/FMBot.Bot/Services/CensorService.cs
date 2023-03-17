using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Discord;
using Discord.WebSocket;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Genius.Models.Song;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;
using Serilog;

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

    public async Task<CensorResult> IsSafeForChannel(IGuild guild, IChannel channel, string artistName, EmbedBuilder embedToUpdate = null)
    {
        var result = await ArtistResult(artistName);
        if (result == CensorResult.NotSafe)
        {
            embedToUpdate?.WithDescription("Sorry, this artist image can't be posted due to it possibly violating Discord ToS.");
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
        
        var censoredArtist = censoredMusic
            .Where(w => w.Artist)
            .FirstOrDefault(f => f.ArtistName.ToLower() == artistName.ToLower());
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

            return CensorResult.Safe;
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
                if (album.CensorType.HasFlag(CensorType.AlbumCoverCensored))
                {
                    return CensorResult.NotSafe;
                }
                if (album.CensorType.HasFlag(CensorType.AlbumCoverNsfw))
                {
                    return CensorResult.Nsfw;
                }

                return CensorResult.Safe;
            }
        }

        return CensorResult.Safe;
    }

    public async Task<CensorResult> ArtistResult(string artistName)
    {
        var censoredMusic = await GetCachedCensoredMusic();
        
        var censoredArtist = censoredMusic
            .Where(w => w.Artist)
            .FirstOrDefault(f => f.ArtistName.ToLower() == artistName.ToLower());

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

            return CensorResult.Safe;
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
        ClearCensoredCache();

        return music;
    }

    public async Task AddArtist(string artistName, CensorType censorType)
    {
        ClearCensoredCache();
        await using var db = await this._contextFactory.CreateDbContextAsync();

        await db.CensoredMusic.AddAsync(new CensoredMusic
        {
            ArtistName = artistName,
            Artist = true,
            SafeForCommands = false,
            SafeForFeatured = false,
            CensorType = censorType
        });

        await db.SaveChangesAsync();
    }

    public async Task AddAlbum(string albumName, string artistName,  CensorType censorType)
    {
        ClearCensoredCache();
        await using var db = await this._contextFactory.CreateDbContextAsync();

        await db.CensoredMusic.AddAsync(new CensoredMusic
        {
            ArtistName = artistName,
            AlbumName = albumName,
            Artist = false,
            SafeForCommands = false,
            SafeForFeatured = false,
            CensorType = censorType
        });

        await db.SaveChangesAsync();
    }

    public async Task Migrate()
    {
        ClearCensoredCache();
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var all = await db.CensoredMusic.Where(w => w.CensorType == CensorType.None).ToListAsync();
        Log.Information($"Found {all.Count} censored things");

        var nsfwArtists = 0;
        var censoredArtists = 0;
        
        var nsfwAlbums = 0;
        var censoredAlbums = 0;
        foreach (var item in all)
        {
            if (item.Artist && item.FeaturedBanOnly != true)
            {
                if (item.SafeForCommands)
                {
                    item.CensorType |= CensorType.ArtistAlbumsNsfw;
                    nsfwArtists++;
                }
                if (!item.SafeForCommands)
                {
                    item.CensorType |= CensorType.ArtistAlbumsCensored;
                    censoredArtists++;
                }
            }
            if (!item.Artist && item.FeaturedBanOnly != true)
            {
                if (item.SafeForCommands)
                {
                    item.CensorType |= CensorType.AlbumCoverNsfw;
                    nsfwAlbums++;
                }
                if (!item.SafeForCommands)
                {
                    item.CensorType |= CensorType.AlbumCoverCensored;
                    censoredAlbums++;
                }
            }
            if (item.FeaturedBanOnly == true && item.Artist)
            {
                item.CensorType |= CensorType.ArtistFeaturedBan;
            }
        }

        Log.Information($"Marked {nsfwArtists} artists as NSFW");
        Log.Information($"Marked {censoredArtists} artists as censored");

        Log.Information($"Marked {nsfwAlbums} albums as NSFW");
        Log.Information($"Marked {censoredAlbums} albums as censored");

        await db.SaveChangesAsync();
    }

    public async Task<CensoredMusicReport> CreateArtistReport(ulong discordUserId, string artistName)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var report = new CensoredMusicReport
        {
            Artist = true,
            ArtistName = artistName,
            ReportStatus = ReportStatus.Pending,
            ReportedAt = DateTime.UtcNow,
            ReportedByDiscordUserId = discordUserId
        };

        //todo save to db

        return report;
    }

    public async Task<CensoredMusicReport> CreateAlbumReport(ulong discordUserId, string albumName, string artistName)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        var report = new CensoredMusicReport
        {
            Artist = false,
            ArtistName = artistName,
            AlbumName = albumName,
            ReportStatus = ReportStatus.Pending,
            ReportedAt = DateTime.UtcNow,
            ReportedByDiscordUserId = discordUserId
        };

        //todo save to db

        return report;
    }
}
