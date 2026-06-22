using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FMBot.Domain.Models;
using FMBot.Persistence.Repositories;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FMBot.Bot.Services;

public class TagService(IMemoryCache cache, IOptions<BotSettings> botSettings)
{
    private readonly BotSettings _botSettings = botSettings.Value;

    private const string BannedTagsCacheKey = "banned-tags";

    public async Task BanTagAsync(string name)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        await TagRepository.SetTagBanned(name, true, connection);
        ClearCache();
    }

    public async Task UnbanTagAsync(string name)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        await TagRepository.SetTagBanned(name, false, connection);
        ClearCache();
    }

    public async Task<List<string>> GetBannedTagsAsync()
    {
        if (cache.TryGetValue(BannedTagsCacheKey, out List<string> bannedTags))
        {
            return bannedTags;
        }

        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        bannedTags = await TagRepository.GetBannedTags(connection);
        cache.Set(BannedTagsCacheKey, bannedTags, TimeSpan.FromMinutes(5));

        return bannedTags;
    }

    private void ClearCache()
    {
        cache.Remove(BannedTagsCacheKey);
    }
}
