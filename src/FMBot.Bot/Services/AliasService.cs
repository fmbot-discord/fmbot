using FMBot.Bot.Models;
using Serilog;
using System.Threading.Tasks;
using System;
using System.Linq;
using FMBot.Domain.Flags;
using Microsoft.Extensions.Caching.Memory;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Services;

public class AliasService
{
    private readonly IMemoryCache _cache;
    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;

    public AliasService(IMemoryCache cache, IDbContextFactory<FMBotDbContext> contextFactory)
    {
        this._cache = cache;
        this._contextFactory = contextFactory;
    }

    public async Task CacheArtistAliases()
    {
        const string cacheKey = "artist-aliases";
        if (this._cache.TryGetValue(cacheKey, out _))
        {
            return;
        }

        this._cache.Set(cacheKey, true, TimeSpan.FromMinutes(10));
        
        var cacheTime = TimeSpan.FromMinutes(20);

        await using var db = await this._contextFactory.CreateDbContextAsync();
        var artistAliases = await db.ArtistAliases
            .AsNoTracking()
            .Where(w => w.CorrectsInScrobbles)
            .Include(i => i.Artist)
            .Select(s => new CachedAlias
            {
                Alias = s.Alias.ToLower(),
                ArtistId = s.ArtistId,
                ArtistName = s.Artist.Name,
                Options = s.Options
            })
            .ToListAsync();

        cacheTime = cacheTime.Add(TimeSpan.FromSeconds(10));

        foreach (var alias in artistAliases)
        {
            this._cache.Set(CacheKeyForFullAlias(alias.Alias), alias, cacheTime);

            if (alias.Options.HasFlag(AliasOption.ApplyInternallyLastfmData))
            {
                this._cache.Set(CacheKeyForDataCorrectionAlias(alias.Alias), alias, cacheTime);
            }
        }

        Log.Information($"Added {artistAliases.Count} artist aliases to memory cache");
    }

    public void RemoveCache()
    {
        this._cache.Remove("artist-aliases");
    }

    public async Task<CachedAlias> GetAlias(string name)
    {
        await CacheArtistAliases();

        return (CachedAlias)this._cache.Get(CacheKeyForFullAlias(name.ToLower()));
    }

    public async Task<CachedAlias> GetDataCorrectionAlias(string name)
    {
        await CacheArtistAliases();

        return (CachedAlias)this._cache.Get(CacheKeyForDataCorrectionAlias(name.ToLower()));
    }

    private static string CacheKeyForFullAlias(string aliasName)
    {
        return $"a-full-alias-{aliasName}";
    }

    private static string CacheKeyForDataCorrectionAlias(string aliasName)
    {
        return $"a-correction-alias-{aliasName}";
    }

    public async Task<ArtistAlias> GetArtistAlias(string aliasName)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        return await db.ArtistAliases
            .Include(i => i.Artist)
            .FirstOrDefaultAsync(f => f.Alias == aliasName.ToLower());
    }

    public async Task<ArtistAlias> GetArtistAliasForId(int aliasId)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();

        return await db.ArtistAliases
            .Include(i => i.Artist)
            .FirstOrDefaultAsync(f => f.Id == aliasId);
    }

    public async Task<ArtistAlias> SetAliasOptions(ArtistAlias aliasToUpdate, AliasOption options)
    {
        await using var db = await this._contextFactory.CreateDbContextAsync();
        var alias = await db.ArtistAliases
            .Include(i => i.Artist)
            .FirstAsync(f => f.Id == aliasToUpdate.Id);

        alias.Options = options;

        db.Update(alias);

        await db.SaveChangesAsync();

        return alias;
    }
}
