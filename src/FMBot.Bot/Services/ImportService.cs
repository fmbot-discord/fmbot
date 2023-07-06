using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Discord;
using FMBot.Bot.Models;
using FMBot.Domain.Enums;
using FMBot.Domain.Models;
using FMBot.Persistence.Domain.Models;
using FMBot.Persistence.Repositories;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FMBot.Bot.Services;

public class ImportService
{
    private readonly HttpClient _httpClient;
    private readonly TimeService _timeService;
    private readonly BotSettings _botSettings;

    public ImportService(HttpClient httpClient, TimeService timeService, IOptions<BotSettings> botSettings)
    {
        this._httpClient = httpClient;
        this._timeService = timeService;
        this._botSettings = botSettings.Value;
    }

    public async Task<List<SpotifyImportModel>> HandleSpotifyFiles(IEnumerable<IAttachment> attachments)
    {
        var spotifyPlays = new List<SpotifyImportModel>();

        foreach (var attachment in attachments.Where(w => w?.Url != null))
        {
            await using var stream = await this._httpClient.GetStreamAsync(attachment.Url);

            var result = await JsonSerializer.DeserializeAsync<List<SpotifyImportModel>>(stream);

            spotifyPlays.AddRange(result);
        }

        return spotifyPlays;
    }

    public async Task<List<UserPlayTs>> SpotifyImportToUserPlays(int userId, List<SpotifyImportModel> spotifyPlays)
    {
        var userPlays = new List<UserPlayTs>();

        var invalidPlays = 0;

        foreach (var spotifyPlay in spotifyPlays
                     .Where(w => w.MasterMetadataAlbumArtistName != null &&
                                 w.MasterMetadataTrackName != null))
        {
            var validScrobble = await this._timeService.IsValidScrobble(
                spotifyPlay.MasterMetadataAlbumArtistName, spotifyPlay.MasterMetadataTrackName, spotifyPlay.MsPlayed);

            if (validScrobble)
            {
                userPlays.Add(new UserPlayTs
                {
                    UserId = userId,
                    TimePlayed = DateTime.SpecifyKind(spotifyPlay.Ts, DateTimeKind.Utc),
                    ArtistName = spotifyPlay.MasterMetadataAlbumArtistName,
                    AlbumName = spotifyPlay.MasterMetadataAlbumAlbumName,
                    TrackName = spotifyPlay.MasterMetadataTrackName,
                    PlaySource = PlaySource.SpotifyImport,
                    MsPlayed = spotifyPlay.MsPlayed
                });
            }
            else
            {
                invalidPlays++;
            }
        }

        return userPlays;
    }

    public async Task<List<UserPlayTs>> RemoveDuplicateSpotifyImports(int userId, IEnumerable<UserPlayTs> userPlays)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        var existingPlays = await PlayRepository.GetUserPlays(userId, connection, 9999999);

        var timestamps = existingPlays
            .Select(s => s.TimePlayed)
            .ToHashSet();

        return userPlays
            .Where(w => !timestamps.Contains(w.TimePlayed))
            .ToList();
    }

    public async Task UpdateExistingPlays(int userId)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        await PlayRepository.SetDefaultSourceForPlays(userId, connection);
    }

    public async Task InsertImportPlays(IEnumerable<UserPlayTs> plays)
    {
        await using var connection = new NpgsqlConnection(this._botSettings.Database.ConnectionString);
        await connection.OpenAsync();

        await PlayRepository.InsertTimeSeriesPlays(plays, connection);
    }
}
