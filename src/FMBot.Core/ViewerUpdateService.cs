using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using FMBot.Domain.Models;
using FMBot.Domain.Types;
using FMBot.Persistence.Repositories;
using Microsoft.Extensions.Options;
using Npgsql;
using Serilog;

namespace FMBot.Core;

public class ViewerUpdateService
{
    private readonly UpdateService _updateService;
    private readonly BotSettings _botSettings;

    public ViewerUpdateService(UpdateService updateService, IOptions<BotSettings> botSettings)
    {
        this._updateService = updateService;
        this._botSettings = botSettings.Value;
    }

    private static readonly ConcurrentDictionary<int, Task<Response<RecentTrackList>>> InFlight = new();

    public async Task<Response<RecentTrackList>> UpdateIfStale(int userId, TimeSpan minimumAge)
    {
        if (InFlight.TryGetValue(userId, out var running))
        {
            return await running;
        }

        var task = Update(userId, minimumAge);
        if (!InFlight.TryAdd(userId, task))
        {
            return await InFlight[userId];
        }

        try
        {
            return await task;
        }
        finally
        {
            InFlight.TryRemove(userId, out _);
        }
    }

    private async Task<Response<RecentTrackList>> Update(int userId, TimeSpan minimumAge)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        if (!await UserRepository.TryClaimUpdateSlot(userId, minimumAge, connection))
        {
            return null;
        }

        try
        {
            return await this._updateService.UpdateUser(new UpdateUserQueueItem(userId));
        }
        catch (Exception e)
        {
            Log.Error(e, "ViewerUpdate: Update failed for {userId}, releasing claim", userId);
            await UserRepository.SetUserUpdateTime(userId, DateTime.UtcNow.AddHours(-2), connection);
            throw;
        }
    }
}
