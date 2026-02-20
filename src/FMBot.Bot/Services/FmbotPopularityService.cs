using System.Threading.Tasks;
using FMBot.Domain.Models;
using FMBot.Persistence.Repositories;
using Npgsql;

namespace FMBot.Bot.Services;

public class FmbotPopularityService
{
    private readonly BotSettings _botSettings;
    public FmbotPopularityService(BotSettings botSettings)
    {
        this._botSettings = botSettings;
    }

    public async Task UpdateAlbumFmbotPopularity()
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await AlbumRepository.UpdateFmbotPopularity(connection);
    }

    public async Task UpdateTrackFmbotPopularity()
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await TrackRepository.UpdateFmbotPopularity(connection);
    }

    public async Task UpdateArtistFmbotPopularity()
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await ArtistRepository.UpdateFmbotPopularity(connection);
    }
}
