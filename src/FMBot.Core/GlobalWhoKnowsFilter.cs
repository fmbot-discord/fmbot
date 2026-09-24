using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FMBot.Domain.Models;
using FMBot.Persistence.EntityFrameWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace FMBot.Core;

public class GlobalWhoKnowsFilter
{
    public const string CacheKey = "global-whoknows-filter-sets";

    private readonly IDbContextFactory<FMBotDbContext> _contextFactory;
    private readonly IMemoryCache _cache;

    public GlobalWhoKnowsFilter(IDbContextFactory<FMBotDbContext> contextFactory, IMemoryCache cache)
    {
        this._contextFactory = contextFactory;
        this._cache = cache;
    }

    public async Task<IList<WhoKnowsObjectWithUser>> FilterGlobalUsersAsync(IEnumerable<WhoKnowsObjectWithUser> users,
        bool qualityFilterDisabled = false)
    {
        if (qualityFilterDisabled)
        {
            return users.ToList();
        }

        var (insensitiveUserNames, userDatesToFilter) = await GetGlobalFilterSetsAsync();

        return users
            .Where(w =>
                !insensitiveUserNames.Contains(w.LastFMUsername)
                &&
                !userDatesToFilter.Contains(w.RegisteredLastFm))
            .ToList();
    }

    public async Task<(HashSet<string> FilteredUserNames, HashSet<DateTime?> FilteredRegisterDates)>
        GetGlobalFilterSetsAsync()
    {
        if (this._cache.TryGetValue(CacheKey,
                out (HashSet<string>, HashSet<DateTime?>) cachedSets))
        {
            return cachedSets;
        }

        await using var db = await this._contextFactory.CreateDbContextAsync();
        var bottedUsers = await db.BottedUsers
            .AsNoTracking()
            .Where(w => w.BanActive)
            .ToListAsync();

        var userNamesToFilter = bottedUsers
            .DistinctBy(d => d.UserNameLastFM, StringComparer.OrdinalIgnoreCase)
            .Select(s => s.UserNameLastFM)
            .ToHashSet();

        var insensitiveUserNames = new HashSet<string>(
            userNamesToFilter, StringComparer.OrdinalIgnoreCase);

        var userDatesToFilter = bottedUsers
            .Where(w => w.LastFmRegistered != null)
            .DistinctBy(d => d.LastFmRegistered)
            .Select(s => s.LastFmRegistered)
            .ToHashSet();

        var existingFilterDate = DateTime.UtcNow.AddMonths(-3);
        var existingRepeatOffenderFilterDate = DateTime.UtcNow.AddMonths(-6);
        var filteredUsers = await db.GlobalFilteredUsers
            .AsNoTracking()
            .Where(w => w.OccurrenceEnd.HasValue
                ? w.OccurrenceEnd.Value > (w.MonthLength == null || w.MonthLength == 3
                    ? existingFilterDate
                    : existingRepeatOffenderFilterDate)
                : w.Created > (w.MonthLength == null || w.MonthLength == 3
                    ? existingFilterDate
                    : existingRepeatOffenderFilterDate))
            .ToListAsync();

        foreach (var filteredUser in filteredUsers)
        {
            insensitiveUserNames.Add(filteredUser.UserNameLastFm);

            if (filteredUser.RegisteredLastFm.HasValue &&
                !userDatesToFilter.Contains(filteredUser.RegisteredLastFm.Value))
            {
                userDatesToFilter.Add(filteredUser.RegisteredLastFm);
            }
        }

        var sets = (insensitiveUserNames, userDatesToFilter);
        this._cache.Set(CacheKey, sets, TimeSpan.FromMinutes(10));

        return sets;
    }
}
