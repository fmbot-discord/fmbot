using System;
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

    public async Task<Response<RecentTrackList>> UpdateIfStale(int userId, TimeSpan minimumAge)
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
